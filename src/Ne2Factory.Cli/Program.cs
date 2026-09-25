using Hangfire;
using Hangfire.Storage.SQLite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Ne2Factory.Cli;
using Ne2Factory.Cli.Agents;
using Ne2Factory.Cli.Backlog;
using Ne2Factory.Cli.Configuration;
using Ne2Factory.Cli.GapScout;
using Ne2Factory.Cli.Services;
using Serilog;

const string TopUsage = """
    Usage: ne2-factory <backlog|gap-scout> ...

    Subcommands:
      backlog     Work the GitHub issues backlog. See `ne2-factory backlog --help`.
      gap-scout   Scan for architecture gaps and file them as issues. See
                    `ne2-factory gap-scout --help`.
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
builder.Services.AddSingleton<IGitHubCli, GitHubCli>();
builder.Services.AddSingleton<IBacklog, GithubIssuesBacklog>();
builder.Services.AddSingleton<BacklogCommand>();
builder.Services.AddSingleton<GapScoutCommand>();
builder.Services.AddHostedService<BacklogWorkerService>();

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
    case "backlog":
        if (rest is ["run", "--yolo"])
        {
            var backlog = services.GetRequiredService<BacklogCommand>();
            backlog.EnsureLabels();
            await host.RunAsync();
            return Environment.ExitCode;
        }
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
