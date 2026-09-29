using Hangfire;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Ne2Factory.Cli.Backlog;

namespace Ne2Factory.Cli.FactoryWorker;

// Hosted alongside Hangfire's own BackgroundJobServer (see AddHangfireServer in
// Program.cs) only when `ne2-factory run` calls host.RunAsync(); other commands
// never start the host, so this never executes for them.
//
// Only discovers eligible issues and enqueues one AgentRunJobs run per issue;
// the actual work (git pull + agent.Run + done/blocked) happens there,
// on Hangfire's background job server.
internal sealed class FactoryWorker(
    IBacklog backlog,
    IAgentRunRepository runs,
    IBackgroundJobClient backgroundJobs,
    ProjectContext ctx,
    ILogger<FactoryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Servidor de jobs en background arrancado ({DbPath}).", ctx.FactoryDbPath);
        logger.LogInformation("Escuchando tickets refinados en el backlog (cada {Interval}s). Ctrl-C para parar.", ctx.BacklogPollIntervalSeconds);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    ProcessQueue();
                }
                catch (Exception ex)
                {
                    // A tick failing (e.g. an unexpected gh/git error not already turned into a
                    // Result) shouldn't take down the whole background service — log and retry
                    // on the next poll instead.
                    logger.LogError(ex, "Fallo procesando la cola del backlog; reintento en el siguiente ciclo.");
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

    private void ProcessQueue()
    {
        var items = backlog.ListPending()
            .OrderBy(i => i.Number)
            .ToArray();

        if (items.Length == 0)
        {
            logger.LogInformation("No hay issues por procesar.");
            return;
        }

        logger.LogInformation("Vistos {Count} tickets en cola: {Numbers}", items.Length, string.Join(", ", items.Select(i => $"#{i.Number}")));

        foreach (var item in items)
        {
            TryEnqueue(item);
        }
    }

    private void TryEnqueue(BacklogItem item)
    {
        var issueNumber = item.Number;
        var agentName = TicketAgents.ForState(item.State);

        if (agentName is null)
        {
            logger.LogDebug("Issue #{Number} está en estado {State}, ya no elegible; la salto.", issueNumber, item.State);
            return;
        }

        // TryCreateQueued locks per issue number regardless of agent name, so an issue
        // can never have a work-ticket and a refine-ticket run active at once.
        var run = runs.TryCreateQueued(issueNumber, agentName);
        if (run is null)
        {
            logger.LogDebug("Issue #{Number} ya tiene un run activo (queued/running); lo salto.", issueNumber);
            return;
        }

        var jobId = backgroundJobs.Enqueue<AgentRunJobs>(job => job.Execute(run.Id));
        logger.LogInformation("Encolado: {JobId} (issue #{Number}, agente {AgentName}, run {RunId}).", jobId, issueNumber, agentName, run.Id);
    }
}
