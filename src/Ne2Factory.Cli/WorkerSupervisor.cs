using Hangfire;
using Hangfire.AspNetCore;
using Hangfire.Storage.Monitoring;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Ne2Factory.Cli.Backlog;
using Ne2Factory.Cli.Verification;

namespace Ne2Factory.Cli;

public enum WorkerState
{
    Stopped,
    Running,
    Stopping,
}

// The worker, as one unit that can be started and stopped at runtime: backlog
// polling (FactoryWorker), pull request verification polling, and the Hangfire
// job server that executes what they (and `gap-scout scan`) enqueue. `ne2-factory
// run` starts it for the life of the process; the control panel turns it on and
// off on demand.
internal sealed class WorkerSupervisor(
    IServiceProvider services,
    IBacklog backlog,
    JobStorage jobStorage,
    ILogger<WorkerSupervisor> logger) : IHostedService
{
    // A Hangfire server heartbeats every 30s; one silent for longer than this is
    // a process that died without deregistering, not a live worker.
    private static readonly TimeSpan HeartbeatTimeout = TimeSpan.FromMinutes(2);

    private readonly object gate = new();
    private RunningWorker? current;

    public WorkerState State
    {
        get
        {
            lock (gate)
                return current is null ? WorkerState.Stopped
                    : current.Stopping is null ? WorkerState.Running
                    : WorkerState.Stopping;
        }
    }

    public DateTime? StartedAt
    {
        get { lock (gate) return current?.StartedAt; }
    }

    public void Start()
    {
        lock (gate)
        {
            if (current is not null)
                throw new InvalidOperationException("El worker ya está en marcha.");

            backlog.EnsureLabels();

            var server = new BackgroundJobServer(
                new BackgroundJobServerOptions
                {
                    // AgentRunJobs and PullRequestVerificationJobs check out a branch and
                    // run the agent against the same working directory, so jobs must run
                    // sequentially, not in parallel.
                    WorkerCount = 1,
                    Activator = new AspNetCoreJobActivator(services.GetRequiredService<IServiceScopeFactory>()),
                },
                jobStorage);

            IHostedService[] pollers =
            [
                ActivatorUtilities.CreateInstance<FactoryWorker.FactoryWorker>(services),
                ActivatorUtilities.CreateInstance<PullRequestVerificationWorker>(services),
            ];
            foreach (var poller in pollers)
                poller.StartAsync(CancellationToken.None).GetAwaiter().GetResult();

            current = new RunningWorker(server, pollers, DateTime.UtcNow);
        }
    }

    // Stops polling right away, but lets a job already running (an agent session)
    // finish: the returned task completes only then.
    public Task StopAsync()
    {
        lock (gate)
        {
            if (current is null) return Task.CompletedTask;
            return current.Stopping ??= StopAsync(current);
        }
    }

    // Hangfire servers alive in other processes (e.g. `ne2-factory run` in a
    // terminal) sharing this repo's job storage — and its working tree.
    public IReadOnlyList<ServerDto> OtherWorkers()
    {
        if (State != WorkerState.Stopped) return [];

        var aliveSince = DateTime.UtcNow - HeartbeatTimeout;
        return jobStorage.GetMonitoringApi().Servers()
            .Where(s => s.Heartbeat > aliveSince)
            .ToArray();
    }

    private async Task StopAsync(RunningWorker worker)
    {
        logger.LogInformation("Parando el worker; si hay un job en curso, se espera a que termine.");
        try
        {
            foreach (var poller in worker.Pollers)
                await poller.StopAsync(CancellationToken.None);

            worker.Server.SendStop();
            await worker.Server.WaitForShutdownAsync(CancellationToken.None);
            worker.Server.Dispose();
            logger.LogInformation("Worker parado.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Fallo parando el worker.");
        }
        finally
        {
            lock (gate) current = null;
        }
    }

    Task IHostedService.StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    // On host shutdown, give up waiting for a running job once the host's own
    // shutdown timeout expires instead of hanging the process.
    async Task IHostedService.StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await StopAsync().WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("El job en curso no terminó a tiempo; se abandona al cerrar.");
        }
    }

    private sealed record RunningWorker(BackgroundJobServer Server, IHostedService[] Pollers, DateTime StartedAt)
    {
        public Task? Stopping { get; set; }
    }
}
