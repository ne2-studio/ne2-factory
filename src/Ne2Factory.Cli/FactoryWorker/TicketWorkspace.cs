using Microsoft.Extensions.Logging;
using Ne2Factory.Cli.Backlog;
using Ne2Factory.Cli.Common;
using Ne2Factory.Cli.FactoryWorker.Publishing;

namespace Ne2Factory.Cli.FactoryWorker;

// The git side of a ticket run, owned by the factory rather than the session:
// the implementer works on its own branch cut from an up-to-date base, the
// factory publishes it for review, and the working tree is always left on the
// base branch for the next run (jobs share one working tree, see Program.cs).
public class TicketWorkspace(IGit git, IChangePublisher publisher, ILogger<TicketWorkspace> logger)
{
    private const string BranchPrefix = "factory/issue-";

    public static string BranchFor(int issueNumber) => $"{BranchPrefix}{issueNumber}";

    // The issue a factory ticket branch belongs to, or null for any other branch.
    public static int? IssueNumberFrom(string branch) =>
        branch.StartsWith(BranchPrefix, StringComparison.Ordinal)
        && int.TryParse(branch[BranchPrefix.Length..], out var number)
            ? number
            : null;

    // Brings the base branch up to date and, when `onTicketBranch`, checks out
    // the ticket branch fresh from it. Returns the base branch.
    public Result<string> Prepare(int issueNumber, bool onTicketBranch)
    {
        var baseBranch = git.DefaultBranch();
        if (baseBranch.IsFailure) return baseBranch;

        if (onTicketBranch)
        {
            // Anything uncommitted would be carried onto the ticket branch and
            // end up in its commits.
            var dirty = git.HasUncommittedChanges();
            if (dirty.IsFailure) return Result.Failure<string>(dirty.Error);
            if (dirty.Value)
                return Result.Failure<string>(ApplicationError.Validation("El working tree tiene cambios sin commitear."));
        }

        var checkedOut = git.Checkout(baseBranch.Value);
        if (checkedOut.IsFailure) return Result.Failure<string>(checkedOut.Error);

        var pulled = git.PullFastForward();
        if (pulled.IsFailure) return Result.Failure<string>(pulled.Error);

        if (onTicketBranch)
        {
            var branched = git.ResetBranch(BranchFor(issueNumber), baseBranch.Value);
            if (branched.IsFailure) return Result.Failure<string>(branched.Error);
        }

        return baseBranch;
    }

    // Checks the session actually left committed work on the ticket branch, and
    // publishes it. Returns where the reviewer finds it.
    public Result<string> Deliver(BacklogItem item, string baseBranch, string? summary)
    {
        var branch = BranchFor(item.Number);

        var dirty = git.HasUncommittedChanges();
        if (dirty.IsFailure) return Result.Failure<string>(dirty.Error);
        if (dirty.Value)
            return Result.Failure<string>(ApplicationError.Validation($"La sesión dejó cambios sin commitear en `{branch}`."));

        var ahead = git.CommitsAhead(baseBranch, branch);
        if (ahead.IsFailure) return Result.Failure<string>(ahead.Error);
        if (ahead.Value == 0)
            return Result.Failure<string>(ApplicationError.Validation($"La rama `{branch}` no tiene commits sobre `{baseBranch}`."));

        return publisher.Publish(new ChangeProposal(item.Number, branch, baseBranch, item.Title, summary ?? ""));
    }

    // Leaves the working tree on the base branch, stashing (not discarding)
    // whatever the session left uncommitted. Never throws: it runs after the
    // outcome is already settled, and a failure here surfaces on the next
    // run's Prepare anyway.
    public void Restore(int issueNumber, string baseBranch, bool discardBranch)
    {
        var branch = BranchFor(issueNumber);

        var dirty = git.HasUncommittedChanges();
        if (dirty is { IsSuccess: true, Value: true })
        {
            var message = $"ne2-factory: cambios sin commitear de #{issueNumber}";
            Warn(git.Stash(message), "stash");
            logger.LogWarning("Cambios sin commitear de #{Number} guardados en el stash ('{Message}').", issueNumber, message);
        }

        Warn(git.Checkout(baseBranch), $"checkout {baseBranch}");
        if (discardBranch)
            Warn(git.DeleteBranch(branch), $"borrar {branch}");
    }

    private void Warn(Result result, string step)
    {
        if (result.IsFailure)
            logger.LogWarning("No se pudo {Step} al restaurar el working tree: {Error}", step, result.Error.Message);
    }
}
