using Hangfire;
using Microsoft.Extensions.Logging;
using Ne2Factory.Cli.Backlog;
using Ne2Factory.Cli.Services;

namespace Ne2Factory.Cli.FactoryWorker;

// The queue-draining logic behind `ne2-factory run` (which stays purely
// interactive) so the worker doesn't depend on a command class. Only
// discovers eligible issues and enqueues one AgentRunJobs run per issue;
// the actual work (git pull + agent.Run + done/blocked) happens there,
// on Hangfire's background job server.
internal sealed class BacklogQueueProcessor(
    IBacklog backlog,
    IBackgroundJobClient backgroundJobs,
    JobStorage jobStorage,
    ILogger<BacklogQueueProcessor> logger)
{
    public void ProcessQueue()
    {
        var issues = backlog.ListRefined()
            .Select(i => i.Number)
            .OrderBy(n => n)
            .ToArray();

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
        var monitoring = jobStorage.GetMonitoringApi();
        if (monitoring.HasJob(job => job.Type == typeof(AgentRunJobs) && job.Args.Count > 0 && job.Args[0] is int n && n == issueNumber))
        {
            logger.LogDebug("Issue #{Number} ya tiene un run encolado o en curso; lo salto.", issueNumber);
            return;
        }

        var jobId = backgroundJobs.Enqueue<AgentRunJobs>(job => job.Execute(issueNumber));
        logger.LogInformation("Encolado: {JobId} (issue #{Number})", jobId, issueNumber);
    }
}
