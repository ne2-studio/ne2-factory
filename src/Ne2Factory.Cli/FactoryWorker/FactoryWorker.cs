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
    ILogger<FactoryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Servidor de jobs en background arrancado ({DbPath}).", ctx.FactoryDbPath);
        logger.LogInformation("Escuchando issues con label '{QueueLabel}' (cada {Interval}s). Ctrl-C para parar.", GithubIssuesBacklog.QueueLabel, ctx.BacklogPollIntervalSeconds);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                queueProcessor.ProcessQueue();
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
