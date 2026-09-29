using Ne2Factory.Cli.Common;

namespace Ne2Factory.Cli.FactoryWorker.Publishing;

// Pushes the ticket branch and opens a pull request against the base branch,
// reusing the open one when the ticket was requeued and run again. "Closes #N"
// in the body is what closes the issue once the reviewer merges it.
internal sealed class GitHubPullRequestPublisher(IGit git, IGitHub gitHub) : IChangePublisher
{
    public Result<string> Publish(ChangeProposal proposal)
    {
        var pushed = git.Push(proposal.Branch);
        if (pushed.IsFailure) return Result.Failure<string>(pushed.Error);

        return gitHub.FindOpenPullRequest(proposal.Branch)
            .Bind(existing => existing is not null
                ? Result.Success(existing)
                : gitHub.CreatePullRequest(proposal.Branch, proposal.BaseBranch, proposal.Title, FormatBody(proposal)))
            .Map(url => $"Pull request: {url}");
    }

    private static string FormatBody(ChangeProposal proposal)
    {
        var summary = string.IsNullOrWhiteSpace(proposal.Summary) ? "(sin resumen)" : proposal.Summary;
        return $"{summary}\n\nCloses #{proposal.IssueNumber}";
    }
}
