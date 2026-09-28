using Hangfire;
using Microsoft.Extensions.Logging;
using Ne2Factory.Cli.Backlog;
using Ne2Factory.Cli.Common;

namespace Ne2Factory.Cli.FactoryWorker;

// The queue-draining logic behind `ne2-factory run` (which stays purely
// interactive) so the worker doesn't depend on a command class. Only
// discovers eligible issues and enqueues one AgentRunJobs run per issue;
// the actual work (git pull + agent.Run + done/blocked) happens there,
// on Hangfire's background job server.
internal sealed class BacklogQueueProcessor(
    IBacklog backlog,
    IAgentRunRepository runs,
    IBackgroundJobClient backgroundJobs,
    ILogger<BacklogQueueProcessor> logger)
{
    private const string WorkTicketAgent = "implementer";
    private const string RefineTicketAgent = "refiner";

    public void ProcessQueue()
    {
        var pending = backlog.ListPending();
        if (pending.IsFailure)
        {
            logger.LogError("No se pudo listar tickets pendientes: {Error}", pending.Error.Message);
            return;
        }

        var issues = pending.Value.Select(i => i.Number).OrderBy(n => n).ToArray();

        if (issues.Length == 0)
        {
            logger.LogInformation("No hay issues por procesar.");
            return;
        }

        logger.LogInformation("Vistos {Count} tickets en cola: {Numbers}", issues.Length, string.Join(", ", issues.Select(n => $"#{n}")));

        foreach (var n in issues)
            TryEnqueue(n);
    }

    private void TryEnqueue(int issueNumber)
    {
        var itemResult = backlog.GetItem(issueNumber);
        if (itemResult.IsFailure)
        {
            logger.LogError("No se pudo consultar el estado de la issue #{Number}: {Error}", issueNumber, itemResult.Error.Message);
            return;
        }

        var item = itemResult.Value;
        if (item is null)
        {
            logger.LogDebug("Issue #{Number} ya no existe; la salto.", issueNumber);
            return;
        }

        var agentName = item.State switch
        {
            TicketState.Refined => WorkTicketAgent,
            TicketState.Unrefined => RefineTicketAgent,
            _ => null,
        };

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
        runs.SetHangfireJobId(run.Id, jobId);
        logger.LogInformation("Encolado: {JobId} (issue #{Number}, agente {AgentName}, run {RunId}).", jobId, issueNumber, agentName, run.Id);
    }
}
