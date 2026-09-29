using Ne2Factory.Cli.Common;
using Ne2Factory.Cli.Services;

namespace Ne2Factory.Cli;

// The git operations the factory drives itself around an agent session
// (branching, publishing, cleanup), so the session only ever has to commit.
public interface IGit
{
    // The remote's default branch (origin/HEAD), without the "origin/" prefix.
    Result<string> DefaultBranch();
    Result Checkout(string branch);
    Result PullFastForward();
    // Creates `branch` at `startPoint` and checks it out, resetting it if it already exists.
    Result ResetBranch(string branch, string startPoint);
    Result<int> CommitsAhead(string baseBranch, string branch);
    Result<bool> HasUncommittedChanges();
    Result Stash(string message);
    Result DeleteBranch(string branch);
    // Pushes `branch` over whatever the factory itself last pushed there, but
    // refuses if the remote branch has anything else (e.g. a reviewer's commits).
    Result Push(string branch);
}

// Runs every command with `-C <root>` rather than relying on the process's cwd.
internal sealed class Git(IProcessRunner proc, RootDirectory root) : IGit
{
    private const string Remote = "origin";
    // Local-only record of the last commit the factory pushed per branch, so a
    // re-run can tell its own previous push apart from someone else's commits.
    private const string PublishedRefPrefix = "refs/ne2-factory/published/";

    public Result<string> DefaultBranch()
    {
        var result = Capture("symbolic-ref", "--short", $"refs/remotes/{Remote}/HEAD");
        if (result.IsFailure)
        {
            return Result.Failure<string>(ApplicationError.ExternalDependencyUnavailable(
                $"{result.Error.Message} (¿falta `git remote set-head {Remote} --auto`?)"));
        }

        var reference = result.Value.Trim();
        var prefix = $"{Remote}/";
        return reference.StartsWith(prefix, StringComparison.Ordinal) ? reference[prefix.Length..] : reference;
    }

    public Result Checkout(string branch) => Run("checkout", branch);

    public Result PullFastForward()
    {
        var exitCode = proc.RunInherited("git", ["-C", root.Path, "pull", "--ff-only"]);
        return exitCode == 0
            ? Result.Success()
            : Result.Failure(ApplicationError.ExternalDependencyUnavailable($"git pull --ff-only falló (exit {exitCode})."));
    }

    public Result ResetBranch(string branch, string startPoint) => Run("checkout", "-B", branch, startPoint);

    public Result<int> CommitsAhead(string baseBranch, string branch) =>
        Capture("rev-list", "--count", $"{baseBranch}..{branch}").Bind(stdout =>
            int.TryParse(stdout.Trim(), out var count)
                ? Result.Success(count)
                : Result.Failure<int>(ApplicationError.ExternalDependencyUnavailable($"git rev-list devolvió algo inesperado: '{stdout.Trim()}'.")));

    public Result<bool> HasUncommittedChanges() =>
        Capture("status", "--porcelain").Map(stdout => stdout.Trim().Length > 0);

    public Result Stash(string message) => Run("stash", "push", "--include-untracked", "-m", message);

    public Result DeleteBranch(string branch) => Run("branch", "-D", branch);

    public Result Push(string branch)
    {
        var remote = Capture("ls-remote", "--heads", Remote, $"refs/heads/{branch}");
        if (remote.IsFailure) return Result.Failure(remote.Error);
        var remoteSha = remote.Value.Split('\t', 2)[0].Trim();

        var publishedRef = PublishedRefPrefix + branch;
        var published = Capture("rev-parse", "--verify", "--quiet", publishedRef);
        var publishedSha = published.IsSuccess ? published.Value.Trim() : "";

        if (remoteSha.Length > 0 && remoteSha != publishedSha)
        {
            return Result.Failure(ApplicationError.Validation(
                $"La rama remota `{branch}` tiene commits que no publicó la factory; no la sobrescribo. Revísala (o bórrala) antes de reencolar."));
        }

        // Lease on the exact commit just checked (empty: must not exist yet), so
        // nothing pushed in between gets overwritten either.
        var pushed = Run("push", $"--force-with-lease={branch}:{remoteSha}", "-u", Remote, branch);
        return pushed.IsSuccess ? Run("update-ref", publishedRef, branch) : pushed;
    }

    private Result Run(params string[] args)
    {
        var result = Capture(args);
        return result.IsSuccess ? Result.Success() : Result.Failure(result.Error);
    }

    private Result<string> Capture(params string[] args)
    {
        var (stdout, stderr, exit) = proc.Capture("git", ["-C", root.Path, .. args]);
        return exit == 0
            ? Result.Success(stdout)
            : Result.Failure<string>(ApplicationError.ExternalDependencyUnavailable(
                $"git {string.Join(' ', args)} falló (exit {exit}): {stderr.Trim()}"));
    }
}
