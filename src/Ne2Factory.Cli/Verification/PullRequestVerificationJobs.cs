using Hangfire;
using Microsoft.Extensions.Logging;
using Ne2Factory.Cli.Agents;
using Ne2Factory.Cli.Backlog;
using Ne2Factory.Cli.FactoryWorker;

namespace Ne2Factory.Cli.Verification;

// Hangfire job body for verifying one pull request: checks out its head, runs
// the verifier agent against it, and posts the verdict as a comment. Nothing
// else is decided from the verdict — it's posted for audit only.
//
// No automatic retries: a failed run is picked up again on the next poll,
// since the pull request's head still lacks a verification comment.
[AutomaticRetry(Attempts = 0)]
public sealed class PullRequestVerificationJobs(
    IPullRequests pullRequests,
    IBacklog backlog,
    IAgentRunRepository runs,
    VerificationWorkspace workspace,
    ICodingAgent codingAgent,
    ILogger<PullRequestVerificationJobs> logger)
{
    public const string AgentName = "verifier";

    public void Execute(Guid runId)
    {
        var run = runs.Get(runId);
        if (run is null)
        {
            logger.LogWarning("AgentRun {RunId} no existe; lo salto.", runId);
            return;
        }

        runs.MarkRunning(runId);

        var number = run.IssueNumber;
        var fetched = pullRequests.Get(number);
        if (fetched.IsFailure)
        {
            logger.LogWarning("-> no se pudo consultar el PR #{Number}: {Error}.", number, fetched.Error.Message);
            runs.Finish(runId, AgentRunStatus.Failed, outcome: null, error: fetched.Error.Message);
            return;
        }

        // It may have been merged, closed, or verified by another run since it was enqueued.
        var pr = fetched.Value;
        if (!PullRequestVerificationWorker.NeedsVerification(pr))
        {
            logger.LogInformation("El PR #{Number} ya no necesita verificación; lo salto.", number);
            runs.Finish(runId, AgentRunStatus.Cancelled, outcome: null, error: "PR ya no elegible (cerrado, draft o ya verificado).");
            return;
        }

        BacklogItem? ticket;
        try
        {
            ticket = backlog.GetItem(TicketWorkspace.IssueNumberFrom(pr.HeadBranch)!.Value);
        }
        catch (BacklogException ex)
        {
            logger.LogWarning("-> no se pudo leer el ticket del PR #{Number}: {Error}.", number, ex.Message);
            runs.Finish(runId, AgentRunStatus.Failed, outcome: null, error: ex.Message);
            return;
        }

        var prepared = workspace.Prepare(pr);
        if (prepared.IsFailure)
        {
            logger.LogWarning("-> no se pudo preparar el repo para el PR #{Number}: {Error}.", number, prepared.Error.Message);
            runs.Finish(runId, AgentRunStatus.Failed, outcome: null, error: prepared.Error.Message);
            return;
        }

        var checkout = prepared.Value;
        VerifierResult? result;
        bool sessionLeftChanges;
        try
        {
            logger.LogInformation("Lanzando @{Agent} en el PR #{Number} ({Url}), commit {Commit}.", AgentName, number, pr.Url, checkout.HeadCommit);
            result = codingAgent.RunWithStructuredOutput<VerifierResult>(
                VerificationPrompt.For(pr, ticket, checkout),
                new CodingAgentOptions { SkipPermissions = true, Agent = AgentName }).Result;
        }
        finally
        {
            sessionLeftChanges = workspace.Restore(number, checkout.DefaultBranch);
        }

        var body = result is null
            ? VerificationComment.ForNoVerdict(checkout.HeadCommit, sessionLeftChanges)
            : VerificationComment.ForVerdict(checkout.HeadCommit, result, sessionLeftChanges);

        var commented = pullRequests.Comment(number, body);
        if (commented.IsFailure)
        {
            logger.LogWarning("-> no se pudo comentar en el PR #{Number}: {Error}.", number, commented.Error.Message);
            runs.Finish(runId, AgentRunStatus.Failed, outcome: result?.Status, error: commented.Error.Message);
            return;
        }

        if (result is null)
        {
            logger.LogWarning("-> sesión sin resultado interpretable en el PR #{Number}.", number);
            runs.Finish(runId, AgentRunStatus.Failed, outcome: null, error: "Sesión sin resultado interpretable");
            return;
        }

        logger.LogInformation("-> verificado: PR #{Number} {Status}", number, result.Status);
        runs.Finish(runId, AgentRunStatus.Succeeded, outcome: result.Status, error: null);
    }
}
