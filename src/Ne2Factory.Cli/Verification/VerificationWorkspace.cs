using Microsoft.Extensions.Logging;
using Ne2Factory.Cli.Common;

namespace Ne2Factory.Cli.Verification;

public sealed record PullRequestCheckout(string DefaultBranch, string BaseCommit, string HeadCommit);

// The git side of a verification run: the verifier reads the pull request's
// head from a detached checkout, and the working tree is always left on the
// default branch for the next run (jobs share one working tree, see Program.cs).
public class VerificationWorkspace(IGit git, ILogger<VerificationWorkspace> logger)
{
    public Result<PullRequestCheckout> Prepare(PullRequest pr)
    {
        var dirty = git.HasUncommittedChanges();
        if (dirty.IsFailure) return Result.Failure<PullRequestCheckout>(dirty.Error);
        if (dirty.Value)
            return Result.Failure<PullRequestCheckout>(ApplicationError.Validation("El working tree tiene cambios sin commitear."));

        var defaultBranch = git.DefaultBranch();
        if (defaultBranch.IsFailure) return Result.Failure<PullRequestCheckout>(defaultBranch.Error);

        var baseCommit = git.Fetch(pr.BaseBranch);
        if (baseCommit.IsFailure) return Result.Failure<PullRequestCheckout>(baseCommit.Error);

        var headCommit = git.Fetch(pr.HeadBranch);
        if (headCommit.IsFailure) return Result.Failure<PullRequestCheckout>(headCommit.Error);

        var checkedOut = git.CheckoutDetached(headCommit.Value);
        if (checkedOut.IsFailure) return Result.Failure<PullRequestCheckout>(checkedOut.Error);

        return new PullRequestCheckout(defaultBranch.Value, baseCommit.Value, headCommit.Value);
    }

    // Leaves the working tree on the default branch, stashing (not discarding)
    // whatever the session left uncommitted. Returns whether it left anything —
    // the verifier is read-only, so that's worth reporting. Never throws: a
    // failure here surfaces on the next run's Prepare anyway.
    public bool Restore(int pullRequestNumber, string defaultBranch)
    {
        var dirty = git.HasUncommittedChanges();
        var leftChanges = dirty is { IsSuccess: true, Value: true };
        if (leftChanges)
        {
            var message = $"ne2-factory: cambios sin commitear de la verificación del PR #{pullRequestNumber}";
            Warn(git.Stash(message), "stash");
            logger.LogWarning("La verificación del PR #{Number} dejó cambios sin commitear; guardados en el stash ('{Message}').", pullRequestNumber, message);
        }

        Warn(git.Checkout(defaultBranch), $"checkout {defaultBranch}");
        return leftChanges;
    }

    private void Warn(Result result, string step)
    {
        if (result.IsFailure)
            logger.LogWarning("No se pudo {Step} al restaurar el working tree: {Error}", step, result.Error.Message);
    }
}
