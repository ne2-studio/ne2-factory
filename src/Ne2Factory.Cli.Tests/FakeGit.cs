using Ne2Factory.Cli.Common;

namespace Ne2Factory.Cli.Tests;

// Tracks just enough repo state (current branch, dirty tree, commits ahead) for
// the factory's branching flow; every call is also recorded in order.
internal sealed class FakeGit : IGit
{
    public List<string> Calls { get; } = [];

    public string CurrentBranch { get; private set; } = "main";
    public bool Dirty { get; set; }
    public int Ahead { get; set; } = 1;
    public HashSet<string> Branches { get; } = ["main"];

    public ApplicationError? DefaultBranchFailure { get; set; }
    public ApplicationError? PullFailure { get; set; }
    public ApplicationError? FetchFailure { get; set; }

    // Commit each remote branch points at, as Fetch reports it.
    public Dictionary<string, string> RemoteBranches { get; } = [];

    public Result<string> DefaultBranch()
    {
        Calls.Add("default-branch");
        return DefaultBranchFailure is { } error ? Result.Failure<string>(error) : Result.Success("main");
    }

    public Result Checkout(string branch)
    {
        Calls.Add($"checkout {branch}");
        CurrentBranch = branch;
        return Result.Success();
    }

    public Result PullFastForward()
    {
        Calls.Add("pull");
        return PullFailure is { } error ? Result.Failure(error) : Result.Success();
    }

    public Result ResetBranch(string branch, string startPoint)
    {
        Calls.Add($"reset-branch {branch} {startPoint}");
        Branches.Add(branch);
        CurrentBranch = branch;
        return Result.Success();
    }

    public Result<int> CommitsAhead(string baseBranch, string branch)
    {
        Calls.Add($"commits-ahead {baseBranch}..{branch}");
        return Result.Success(Ahead);
    }

    public Result<bool> HasUncommittedChanges()
    {
        Calls.Add("status");
        return Result.Success(Dirty);
    }

    public Result Stash(string message)
    {
        Calls.Add("stash");
        Dirty = false;
        return Result.Success();
    }

    public Result DeleteBranch(string branch)
    {
        Calls.Add($"delete-branch {branch}");
        Branches.Remove(branch);
        return Result.Success();
    }

    public Result Push(string branch)
    {
        Calls.Add($"push {branch}");
        return Result.Success();
    }

    public Result<string> Fetch(string branch)
    {
        Calls.Add($"fetch {branch}");
        return FetchFailure is { } error ? Result.Failure<string>(error) : Result.Success(RemoteBranches[branch]);
    }

    public Result CheckoutDetached(string commit)
    {
        Calls.Add($"checkout --detach {commit}");
        CurrentBranch = commit;
        return Result.Success();
    }
}
