using Hangfire;
using Microsoft.Extensions.Logging;
using Ne2Factory.Cli.Backlog;

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
    private const string WorkTicketAgent = "work-ticket";
    private const string RefineTicketAgent = "refine-ticket";

    public void ProcessQueue()
    {
        ProcessIssues("refinados", backlog.ListRefined(), WorkTicketAgent);
        ProcessIssues("sin refinar", backlog.ListUnrefined(), RefineTicketAgent);
    }

    private void ProcessIssues(string label, IReadOnlyList<BacklogItem> items, string agentName)
    {
        var issues = items.Select(i => i.Number).OrderBy(n => n).ToArray();

        if (issues.Length == 0)
        {
            logger.LogInformation("No hay issues {Label} por procesar.", label);
            return;
        }

        logger.LogInformation("Vistos {Count} tickets {Label} en cola: {Numbers}", issues.Length, label, string.Join(", ", issues.Select(n => $"#{n}")));

        foreach (var n in issues)
            TryEnqueue(n, agentName);
    }

    private void TryEnqueue(int issueNumber, string agentName)
    {
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
