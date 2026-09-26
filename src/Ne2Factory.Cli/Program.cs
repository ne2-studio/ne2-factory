using Hangfire;
using Hangfire.Storage.SQLite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Ne2Factory.Cli;
using Ne2Factory.Cli.Agents;
using Ne2Factory.Cli.Backlog;
using Ne2Factory.Cli.Configuration;
using Ne2Factory.Cli.FactoryWorker;
using Ne2Factory.Cli.GapScout;
using Ne2Factory.Cli.Services;
using Serilog;

const string TopUsage = """
    Usage: ne2-factory <run|backlog|gap-scout> ...

    Subcommands:
      run         Start the worker in the foreground: polls the backlog queue and
                    hosts the background job server. See `ne2-factory run --help`.
      backlog     Interactively inspect/manage the GitHub issues backlog. See
                    `ne2-factory backlog --help`.
      gap-scout   Scan for architecture gaps and file them as issues. See
                    `ne2-factory gap-scout --help`.
    """;

const string RunUsage = """
    Usage: ne2-factory run   (from the root of the repo being worked on)

    Starts the worker in the foreground: polls the queue every 30s
    (BACKLOG_POLL_INTERVAL to override) and keeps running, picking up tickets
    labeled both "backlog" and "refined" — no need to restart it per ticket.
    Also hosts the background job server that processes `gap-scout scan` jobs
    queued while it runs. Stop with Ctrl-C.

    Every ticket runs headless via `claude --print --dangerously-skip-permissions`,
    so there is never anyone to answer a prompt — only run this when you're
    comfortable leaving it fully unattended.

    The worker stops (exits) if a ticket ends without a clear done/blocked
    signal — that needs a human look before resuming; requeue/fix the issue
    and run `ne2-factory run` again.

    Use `ne2-factory backlog` to inspect/manage the queue interactively.
    Requires `gh` authenticated against this repo.
    """;

var rootDir = RootDirectoryResolver.Resolve();
Directory.SetCurrentDirectory(rootDir);

var builder = Host.CreateApplicationBuilder(args);
builder.Configuration.AddEnvFile(Path.Combine(rootDir, ".ne2-factory.env"));

builder.Services.AddSerilog((sp, loggerConfig) => loggerConfig
    .MinimumLevel.Information()
    .Enrich.FromLogContext()
    .WriteTo.Console());

builder.Services.AddSingleton(new RootDirectory(rootDir));
builder.Services.AddSingleton<ProjectContext>();
builder.Services.AddSingleton<IProcessRunner, ProcessRunner>();
builder.Services.AddSingleton<IAgent, ClaudeAgent>();
builder.Services.AddSingleton<IAgentSignalChannel, FileAgentSignalChannel>();
builder.Services.AddSingleton<IGitHubCli, GitHubCli>();
builder.Services.AddSingleton<IBacklog, GithubIssuesBacklog>();
builder.Services.AddSingleton<BacklogCommand>();
builder.Services.AddSingleton<BacklogQueueProcessor>();
builder.Services.AddSingleton<GapScoutCommand>();
builder.Services.AddHostedService<FactoryWorker>();

var backlogDbDir = Path.Combine(rootDir, ".backlog");
Directory.CreateDirectory(backlogDbDir);
var gapScoutDbPath = Path.Combine(backlogDbDir, "gap-scout.db");
builder.Services.AddHangfire(config => config.UseSQLiteStorage(gapScoutDbPath));
builder.Services.AddHangfireServer();

using var host = builder.Build();
using var scope = host.Services.CreateScope();
var services = scope.ServiceProvider;
var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("ne2-factory");

if (args.Length == 0)
{
    logger.LogInformation("{Text}", TopUsage);
    return 0;
}

var rest = args.Skip(1).ToArray();

switch (args[0])
{
    case "run":
        if (rest is ["-h"] or ["--help"])
        {
            logger.LogInformation("{Text}", RunUsage);
            return 0;
        }
        services.GetRequiredService<IBacklog>().EnsureLabels();
        await host.RunAsync();
        return Environment.ExitCode;
    case "backlog":
        return services.GetRequiredService<BacklogCommand>().Run(rest);
    case "gap-scout":
        return services.GetRequiredService<GapScoutCommand>().Run(rest);
    case "-h" or "--help":
        logger.LogInformation("{Text}", TopUsage);
        return 0;
    default:
        logger.LogError("Comando desconocido: {Command}", args[0]);
        logger.LogInformation("{Text}", TopUsage);
        return 1;
}
