using Hangfire;
using Hangfire.Storage.SQLite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Ne2Factory.Cli.Agents;
using Ne2Factory.Cli.Backlog;
using Ne2Factory.Cli.Backlog.File;
using Ne2Factory.Cli.Backlog.GitHub;
using Ne2Factory.Cli.Configuration;
using Ne2Factory.Cli.FactoryWorker;
using Ne2Factory.Cli.FactoryWorker.Publishing;
using Ne2Factory.Cli.GapScout;
using Ne2Factory.Cli.Services;
using Ne2Factory.Cli.Verification;
using Serilog;

namespace Ne2Factory.Cli;

// Composition shared by every entry point — the CLI subcommands and the control
// panel (`ne2-factory ui`) — so both drive the very same factory.
internal static class FactoryServices
{
    public static void AddFactory(
        this IHostApplicationBuilder builder,
        string rootDir,
        Action<LoggerConfiguration>? configureLogging = null)
    {
        builder.Configuration.AddEnvFile(Path.Combine(rootDir, ".ne2-factory.env"));
        builder.Configuration.AddJsonFile(Path.Combine(rootDir, "appsettings.Local.json"), optional: true, reloadOnChange: false);

        builder.Services.AddSerilog((sp, loggerConfig) =>
        {
            loggerConfig
                .MinimumLevel.Information()
                // Hangfire logs at Info are noisy and drown out the worker's own logs.
                // Bump to Warning by default; lower this back to Information/Debug locally
                // when Hangfire internals need debugging.
                .MinimumLevel.Override("Hangfire", Serilog.Events.LogEventLevel.Warning)
                .Enrich.FromLogContext()
                .WriteTo.Console();
            configureLogging?.Invoke(loggerConfig);
        });

        var services = builder.Services;
        services.AddSingleton(new RootDirectory(rootDir));
        services.AddSingleton<ProjectContext>();
        services.AddSingleton<IProcessRunner, ProcessRunner>();
        services.AddSingleton<ICodingAgent, ClaudeCodingAgent>();
        services.AddSingleton<IGitHub, GitHub>();
        services.AddSingleton<IGit, Git>();
        services.AddSingleton<IBacklog>(sp =>
        {
            var ctx = sp.GetRequiredService<ProjectContext>();
            return ctx.BacklogProvider.Equals("File", StringComparison.OrdinalIgnoreCase)
                ? new FileBacklog(ctx)
                : new GithubIssuesBacklog(sp.GetRequiredService<IGitHub>());
        });
        // Follows the backlog provider: GitHub issues get a GitHub pull request, the
        // File backlog has nowhere to open one and keeps the branch local.
        services.AddSingleton<IChangePublisher>(sp =>
        {
            var ctx = sp.GetRequiredService<ProjectContext>();
            return ctx.BacklogProvider.Equals("File", StringComparison.OrdinalIgnoreCase)
                ? new LocalBranchPublisher()
                : new GitHubPullRequestPublisher(sp.GetRequiredService<IGit>(), sp.GetRequiredService<IGitHub>());
        });
        services.AddSingleton<TicketWorkspace>();
        services.AddSingleton<IAgentRunRepository, SqliteAgentRunRepository>();
        services.AddSingleton<IPullRequests, GitHubPullRequests>();
        services.AddSingleton<VerificationWorkspace>();
        services.AddSingleton<GapScoutScans>();
        services.AddScoped<Agent>();

        // Not started with the host: whoever needs the worker starts it explicitly
        // (`run` right away, the control panel on demand). Registered as a hosted
        // service only so host shutdown (Ctrl-C) stops it.
        services.AddSingleton<WorkerSupervisor>();
        services.AddHostedService(sp => sp.GetRequiredService<WorkerSupervisor>());

        var dataDir = ProjectContext.DataDirFor(rootDir);
        Directory.CreateDirectory(dataDir);
        // Storage and client only; the job server is part of the worker (see WorkerSupervisor).
        services.AddHangfire(config => config.UseSQLiteStorage(Path.Combine(dataDir, "database.db")));
    }
}
