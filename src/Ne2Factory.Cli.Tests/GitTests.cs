namespace Ne2Factory.Cli.Tests;

public class GitTests
{
    private const string Root = "/repo";

    private readonly FakeProcessRunner _proc = new();
    private readonly Git _git;

    public GitTests()
    {
        _git = new Git(_proc, new RootDirectory(Root));
    }

    [Fact]
    public void DefaultBranch_StripsRemotePrefix_FromOriginHead()
    {
        _proc.OnCapture = (_, _, _) => ("origin/main\n", "", 0);

        var result = _git.DefaultBranch();

        Assert.Equal("main", result.Value);
        Assert.Equal(["-C", Root, "symbolic-ref", "--short", "refs/remotes/origin/HEAD"], _proc.CaptureCalls.Single().Args);
    }

    [Fact]
    public void DefaultBranch_Fails_WhenOriginHeadIsNotSet()
    {
        _proc.OnCapture = (_, _, _) => ("", "fatal: ref refs/remotes/origin/HEAD is not a symbolic ref", 128);

        var result = _git.DefaultBranch();

        Assert.True(result.IsFailure);
        Assert.Contains("git remote set-head origin --auto", result.Error.Message);
    }

    [Fact]
    public void HasUncommittedChanges_IsTrue_WhenStatusReportsAnything()
    {
        _proc.OnCapture = (_, _, _) => ("?? nuevo.txt\n", "", 0);

        Assert.True(_git.HasUncommittedChanges().Value);
    }

    [Fact]
    public void HasUncommittedChanges_IsFalse_WhenStatusIsEmpty()
    {
        Assert.False(_git.HasUncommittedChanges().Value);
    }

    [Fact]
    public void CommitsAhead_ParsesRevListCount()
    {
        _proc.OnCapture = (_, _, _) => ("3\n", "", 0);

        var result = _git.CommitsAhead("main", "factory/issue-42");

        Assert.Equal(3, result.Value);
        Assert.Equal(["-C", Root, "rev-list", "--count", "main..factory/issue-42"], _proc.CaptureCalls.Single().Args);
    }

    [Fact]
    public void Push_Fails_WithStderr_WhenGitPushFails()
    {
        _proc.OnCapture = (_, args, _) => args[2] == "push" ? ("", "rejected: stale info", 1) : ("", "", 0);

        var result = _git.Push("factory/issue-42");

        Assert.True(result.IsFailure);
        Assert.Contains("rejected: stale info", result.Error.Message);
        Assert.DoesNotContain(_proc.CaptureCalls, c => c.Args[2] == "update-ref");
    }

    [Fact]
    public void Push_LeasesOnRemoteSha_AndRecordsWhatItPublished()
    {
        _proc.OnCapture = (_, args, _) => args[2] switch
        {
            "ls-remote" => ("abc123\trefs/heads/factory/issue-42\n", "", 0),
            "rev-parse" => ("abc123\n", "", 0),
            _ => ("", "", 0),
        };

        var result = _git.Push("factory/issue-42");

        Assert.True(result.IsSuccess);
        var calls = _proc.CaptureCalls.Select(c => c.Args.Skip(2).ToArray()).ToArray();
        Assert.Contains(calls, a => a.SequenceEqual(new[] { "push", "--force-with-lease=factory/issue-42:abc123", "-u", "origin", "factory/issue-42" }));
        Assert.Contains(calls, a => a.SequenceEqual(new[] { "update-ref", "refs/ne2-factory/published/factory/issue-42", "factory/issue-42" }));
    }

    [Fact]
    public void Push_RefusesWithoutPushing_WhenRemoteBranchHasCommitsTheFactoryDidNotPublish()
    {
        _proc.OnCapture = (_, args, _) => args[2] switch
        {
            "ls-remote" => ("def456\trefs/heads/factory/issue-42\n", "", 0),
            "rev-parse" => ("abc123\n", "", 0),
            _ => ("", "", 0),
        };

        var result = _git.Push("factory/issue-42");

        Assert.True(result.IsFailure);
        Assert.Contains("no publicó la factory", result.Error.Message);
        Assert.DoesNotContain(_proc.CaptureCalls, c => c.Args[2] == "push");
    }
}
