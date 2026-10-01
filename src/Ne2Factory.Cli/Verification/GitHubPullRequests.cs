using Ne2Factory.Cli.Common;

namespace Ne2Factory.Cli.Verification;

// Only place that maps IGitHub's pull request shape onto the verification domain.
internal sealed class GitHubPullRequests(IGitHub gitHub) : IPullRequests
{
    public Result<IReadOnlyList<PullRequest>> ListOpen() =>
        gitHub.ListOpenPullRequests().Map(prs => (IReadOnlyList<PullRequest>)prs.Select(ToPullRequest).ToArray());

    public Result<PullRequest> Get(int number) => gitHub.ViewPullRequest(number).Map(ToPullRequest);

    public Result Comment(int number, string body) => gitHub.CommentOnPullRequest(number, body);

    private static PullRequest ToPullRequest(PullRequestDetail pr) => new(
        pr.Number,
        pr.Title,
        pr.Body,
        pr.Url,
        string.Equals(pr.State, "OPEN", StringComparison.OrdinalIgnoreCase),
        pr.IsDraft,
        pr.HeadRefName,
        pr.HeadRefOid,
        pr.BaseRefName,
        (pr.Comments ?? []).Select(c => c.Body).ToArray());
}
