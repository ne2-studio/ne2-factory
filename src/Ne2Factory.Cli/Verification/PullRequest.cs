using Ne2Factory.Cli.Common;

namespace Ne2Factory.Cli.Verification;

public sealed record PullRequest(
    int Number,
    string Title,
    string? Body,
    string Url,
    bool IsOpen,
    bool IsDraft,
    string HeadBranch,
    string HeadCommit,
    string BaseBranch,
    IReadOnlyList<string> Comments);

// The pull requests the factory reads and comments on. Unlike IBacklog, failures
// come back as Result: every caller here already turns them into a run outcome.
public interface IPullRequests
{
    Result<IReadOnlyList<PullRequest>> ListOpen();
    Result<PullRequest> Get(int number);
    Result Comment(int number, string body);
}
