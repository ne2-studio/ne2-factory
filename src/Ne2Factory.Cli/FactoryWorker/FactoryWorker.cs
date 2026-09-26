using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Ne2Factory.Cli.Backlog;

namespace Ne2Factory.Cli.FactoryWorker;

// Hosted alongside Hangfire's own BackgroundJobServer (see AddHangfireServer in
// Program.cs) only when `ne2-factory run` calls host.RunAsync(); other commands
// never start the host, so this never executes for them.
internal sealed class FactoryWorker(
    BacklogQueueProcessor queueProcessor,
    ProjectContext ctx,
    IHostApplicationLifetime lifetime,
    ILogger<FactoryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Servidor de jobs en background arrancado (gap-scout scan usa {DbPath}).", ctx.GapScoutDbPath);
        logger.LogInformation("Escuchando issues con label '{QueueLabel}' (cada {Interval}s). Ctrl-C para parar.", GithubIssuesBacklog.QueueLabel, ctx.BacklogPollIntervalSeconds);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                if (!queueProcessor.ProcessQueue())
                {
                    logger.LogWarning("Worker detenido (revisión manual necesaria antes de reanudar).");
                    Environment.ExitCode = 1;
                    lifetime.StopApplication();
                    return;
                }
                logger.LogInformation("Durmiendo {Interval}s antes de volver a comprobar la cola.", ctx.BacklogPollIntervalSeconds);
                await Task.Delay(TimeSpan.FromSeconds(ctx.BacklogPollIntervalSeconds), stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Ctrl-C / host shutdown requested — nothing more to do.
        }
    }
}
