using Ne2Factory.Cli.Common;

namespace Ne2Factory.Cli.FactoryWorker.Publishing;

// The File backlog has no code host to open a pull request on: the ticket
// branch stays local, unpushed, for the reviewer to inspect and merge by hand.
internal sealed class LocalBranchPublisher : IChangePublisher
{
    public Result<string> Publish(ChangeProposal proposal) =>
        Result.Success($"Rama local: `{proposal.Branch}` (sin publicar)");
}
