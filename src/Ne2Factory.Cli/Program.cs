using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Ne2Factory.Cli;
using Ne2Factory.Cli.Backlog.Commands;
using Ne2Factory.Cli.ControlPanel;
using Ne2Factory.Cli.FactoryWorker.Commands;
using Ne2Factory.Cli.GapScout;

const string TopUsage = """
    Usage: ne2-factory <run|ui|backlog|runs|gap-scout> ...

    Subcommands:
      run         Start the worker in the foreground: polls the backlog queue and
                    hosts the background job server. See `ne2-factory run --help`.
      ui          Serve the web control panel: everything below, plus starting and
                    stopping the worker. See `ne2-factory ui --help`.
      backlog     Interactively inspect/manage the ticket backlog. See
                    `ne2-factory backlog --help`.
      runs        List recent AgentRuns for observability. See
                    `ne2-factory runs --help`.
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

    Each implemented ticket lands on its own branch (factory/issue-<n>, cut from
    origin's default branch) and is handed over as a pull request; the issue is
    labeled "in-review" and closes when the PR is merged. The working tree must
    be clean, and is left on the default branch after every ticket.

    The worker stops (exits) if a ticket ends without a clear done/blocked
    signal — that needs a human look before resuming; requeue/fix the issue
    and run `ne2-factory run` again.

    Each open pull request the factory opened (not draft) is also verified by
    the `verifier` agent, once per new head commit, and its verdict posted as
    a comment on the pull request — for audit only, nothing else is decided
    from it. Skipped with the File backlog, which opens no pull requests.

    Use `ne2-factory backlog` to inspect/manage the queue interactively.
    Requires `gh` authenticated against this repo when using the GitHub
    backlog backend (see `ne2-factory backlog --help`).
    """;

var rootDir = RootDirectoryResolver.Resolve();
Directory.SetCurrentDirectory(rootDir);

if (args is ["ui", .. var uiArgs])
    return await ControlPanelCommand.RunAsync(rootDir, uiArgs);

var builder = Host.CreateApplicationBuilder(args);
builder.AddFactory(rootDir);
builder.Services.AddSingleton<BacklogCommand>();
builder.Services.AddSingleton<RunsCommand>();
builder.Services.AddSingleton<GapScoutCommand>();

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
        services.GetRequiredService<WorkerSupervisor>().Start();
        await host.RunAsync();
        return Environment.ExitCode;
    case "backlog":
        return services.GetRequiredService<BacklogCommand>().Run(rest);
    case "runs":
        return services.GetRequiredService<RunsCommand>().Run(rest);
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
