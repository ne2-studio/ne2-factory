using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Ne2Factory.Cli.Backlog;

// Hosted alongside Hangfire's own BackgroundJobServer (see AddHangfireServer in
// Program.cs) only when `backlog run --yolo` calls host.RunAsync(); other
// commands never start the host, so this never executes for them.
internal sealed class BacklogWorkerService(
    BacklogCommand backlogCommand,
    ProjectContext ctx,
    IHostApplicationLifetime lifetime,
    ILogger<BacklogWorkerService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Servidor de jobs en background arrancado (gap-scout scan usa {DbPath}).", ctx.GapScoutDbPath);
        logger.LogInformation("Escuchando issues con label '{QueueLabel}' (cada {Interval}s). Ctrl-C para parar.", BacklogCommand.QueueLabel, ctx.BacklogPollIntervalSeconds);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                if (!backlogCommand.ProcessQueue())
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
