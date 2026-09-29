using Ne2Factory.Cli.Common;

namespace Ne2Factory.Cli.FactoryWorker.Publishing;

public sealed record ChangeProposal(int IssueNumber, string Branch, string BaseBranch, string Title, string Summary);

// Hands a ticket branch over to the reviewer. Which backend (a GitHub pull
// request, a local branch, ...) follows the backlog provider, same as IBacklog.
public interface IChangePublisher
{
    // Returns a short, human-readable pointer to where the reviewer finds the
    // change (e.g. the PR URL), for the ticket's comment.
    Result<string> Publish(ChangeProposal proposal);
}
