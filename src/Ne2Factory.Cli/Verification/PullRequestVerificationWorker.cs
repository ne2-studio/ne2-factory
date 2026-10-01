using Hangfire;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Ne2Factory.Cli.FactoryWorker;

namespace Ne2Factory.Cli.Verification;

// Hosted alongside FactoryWorker by `ne2-factory run`. Only discovers the
// factory's own pull requests whose head hasn't been verified yet and enqueues
// one PullRequestVerificationJobs run per pull request; the verification itself
// happens there, on the same single-worker Hangfire server as ticket runs.
internal sealed class PullRequestVerificationWorker(
    IPullRequests pullRequests,
    IAgentRunRepository runs,
    IBackgroundJobClient backgroundJobs,
    ProjectContext ctx,
    ILogger<PullRequestVerificationWorker> logger) : BackgroundService
{
    // Open, ready for review, opened by the factory for a ticket, and its
    // current head not verified yet.
    public static bool NeedsVerification(PullRequest pr) =>
        pr.IsOpen
        && !pr.IsDraft
        && TicketWorkspace.IssueNumberFrom(pr.HeadBranch) is not null
        && !VerificationComment.Covers(pr);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // The File backlog keeps ticket branches local: there are no pull requests to verify.
        if (ctx.BacklogProvider.Equals("File", StringComparison.OrdinalIgnoreCase))
            return;

        logger.LogInformation("Escuchando PRs de la factory por verificar (cada {Interval}s).", ctx.BacklogPollIntervalSeconds);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    ProcessOpenPullRequests();
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Fallo buscando PRs por verificar; reintento en el siguiente ciclo.");
                }
                await Task.Delay(TimeSpan.FromSeconds(ctx.BacklogPollIntervalSeconds), stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Ctrl-C / host shutdown requested — nothing more to do.
        }
    }

    private void ProcessOpenPullRequests()
    {
        var open = pullRequests.ListOpen();
        if (open.IsFailure)
        {
            logger.LogWarning("No se pudieron listar los PRs abiertos: {Error}", open.Error.Message);
            return;
        }

        foreach (var pr in open.Value.Where(NeedsVerification))
        {
            // Pull requests and issues share GitHub's numbering, so the per-number
            // lock never collides with a ticket run.
            var run = runs.TryCreateQueued(pr.Number, PullRequestVerificationJobs.AgentName);
            if (run is null)
            {
                logger.LogDebug("El PR #{Number} ya tiene una verificación activa; lo salto.", pr.Number);
                continue;
            }

            var jobId = backgroundJobs.Enqueue<PullRequestVerificationJobs>(job => job.Execute(run.Id));
            logger.LogInformation("Encolada verificación: {JobId} (PR #{Number}, commit {Commit}, run {RunId}).", jobId, pr.Number, pr.HeadCommit, run.Id);
        }
    }
}
