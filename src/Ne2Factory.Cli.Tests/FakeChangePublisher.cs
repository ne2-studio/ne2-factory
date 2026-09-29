using Ne2Factory.Cli.Common;
using Ne2Factory.Cli.FactoryWorker.Publishing;

namespace Ne2Factory.Cli.Tests;

internal sealed class FakeChangePublisher : IChangePublisher
{
    public List<ChangeProposal> Published { get; } = [];

    public ApplicationError? PublishFailure { get; set; }

    public Result<string> Publish(ChangeProposal proposal)
    {
        if (PublishFailure is { } error) return Result.Failure<string>(error);

        Published.Add(proposal);
        return Result.Success($"Pull request: https://example.com/pull/{proposal.IssueNumber}");
    }
}
