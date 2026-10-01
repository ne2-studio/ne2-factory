using Ne2Factory.Cli.Common;
using Ne2Factory.Cli.Verification;

namespace Ne2Factory.Cli.Tests;

internal sealed class InMemoryPullRequests : IPullRequests
{
    private readonly Dictionary<int, PullRequest> pullRequests = [];

    public ApplicationError? GetFailure { get; set; }
    public ApplicationError? CommentFailure { get; set; }

    public void Add(PullRequest pr) => pullRequests[pr.Number] = pr;

    public PullRequest? Find(int number) => pullRequests.GetValueOrDefault(number);

    public Result<IReadOnlyList<PullRequest>> ListOpen() =>
        Result.Success<IReadOnlyList<PullRequest>>(pullRequests.Values.Where(pr => pr.IsOpen).ToArray());

    public Result<PullRequest> Get(int number) =>
        GetFailure is { } error
            ? Result.Failure<PullRequest>(error)
            : pullRequests.TryGetValue(number, out var pr)
                ? Result.Success(pr)
                : Result.Failure<PullRequest>(ApplicationError.NotFound($"PR #{number} no existe."));

    public Result Comment(int number, string body)
    {
        if (CommentFailure is { } error) return Result.Failure(error);

        var pr = pullRequests[number];
        pullRequests[number] = pr with { Comments = [.. pr.Comments, body] };
        return Result.Success();
    }
}
