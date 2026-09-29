using Microsoft.Extensions.Logging.Abstractions;
using Ne2Factory.Cli.Backlog;
using Ne2Factory.Cli.FactoryWorker;
using Ne2Factory.Cli.FactoryWorker.Publishing;
using Ne2Factory.Cli.Services;

namespace Ne2Factory.Cli.Tests;

// Git and TicketWorkspace against real repos in a temp dir: a bare "origin",
// the factory's clone, and a second clone standing in for anyone else pushing
// to the same remote. Needs `git` on PATH; nothing leaves the machine.
public sealed class GitIntegrationTests : IDisposable
{
    private const string TicketBranch = "factory/issue-1";
    private static readonly BacklogItem Ticket = new(1, "Título", null, null, TicketState.Refined, []);

    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"ne2-git-{Guid.NewGuid():N}");
    private readonly string _remote;
    private readonly string _work;
    private readonly string _other;
    private readonly ProcessRunner _proc = new(NullLogger<ProcessRunner>.Instance);
    private readonly Git _git;

    public GitIntegrationTests()
    {
        _remote = Path.Combine(_tempDir, "remote.git");
        _work = Path.Combine(_tempDir, "work");
        _other = Path.Combine(_tempDir, "other");
        var seed = Path.Combine(_tempDir, "seed");

        GitIn(_tempDir, "init", "--bare", "-b", "main", _remote);
        GitIn(_tempDir, "init", "-b", "main", seed);
        Configure(seed);
        CommitFile(seed, "README.md", "hola");
        GitIn(seed, "push", _remote, "main");

        GitIn(_tempDir, "clone", _remote, _work);
        GitIn(_tempDir, "clone", _remote, _other);
        Configure(_work);
        Configure(_other);

        _git = new Git(_proc, new RootDirectory(_work));
    }

    public void Dispose()
    {
        // Git marks object files read-only, which Directory.Delete refuses on some platforms.
        foreach (var file in Directory.EnumerateFiles(_tempDir, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void DefaultBranch_ReadsOriginHead()
    {
        Assert.Equal("main", _git.DefaultBranch().Value);
    }

    [Fact]
    public void DefaultBranch_Fails_WhenOriginHeadIsUnset()
    {
        GitIn(_work, "remote", "set-head", "origin", "-d");

        var result = _git.DefaultBranch();

        Assert.True(result.IsFailure);
        Assert.Contains("git remote set-head origin --auto", result.Error.Message);
    }

    [Fact]
    public void TicketBranch_CanBeCutCommittedCountedAndPushed()
    {
        Assert.True(_git.ResetBranch(TicketBranch, "main").IsSuccess);
        Assert.Equal(TicketBranch, CurrentBranch(_work));
        Assert.Equal(0, _git.CommitsAhead("main", TicketBranch).Value);

        File.WriteAllText(Path.Combine(_work, "cambio.txt"), "x");
        Assert.True(_git.HasUncommittedChanges().Value);

        CommitAll(_work, "cambio");
        Assert.False(_git.HasUncommittedChanges().Value);
        Assert.Equal(1, _git.CommitsAhead("main", TicketBranch).Value);

        Assert.True(_git.Push(TicketBranch).IsSuccess);
        Assert.Equal(Head(_work, TicketBranch), RemoteHead(TicketBranch));
    }

    [Fact]
    public void ReRun_CutsBranchAgainFromUpdatedBase_AndForcePushesOverPreviousRun()
    {
        CutCommitAndPush("primer intento");
        _git.Checkout("main");
        PushToMainFromOther("main avanza");

        Assert.True(_git.PullFastForward().IsSuccess);
        Assert.True(_git.ResetBranch(TicketBranch, "main").IsSuccess);
        Assert.Equal(Head(_work, "main"), Head(_work, TicketBranch));
        Assert.Equal(Head(_other, "main"), Head(_work, TicketBranch));

        CommitFile(_work, "segundo.txt", "y");
        Assert.True(_git.Push(TicketBranch).IsSuccess);
        Assert.Equal(Head(_work, TicketBranch), RemoteHead(TicketBranch));
    }

    [Fact]
    public void Push_IsRefused_WhenSomeoneElsePushedToTicketBranch()
    {
        CutCommitAndPush("primer intento");
        _git.Checkout("main");
        var reviewerCommit = PushToTicketBranchFromOther("commit del revisor");

        _git.ResetBranch(TicketBranch, "main");
        CommitFile(_work, "segundo.txt", "y");
        var result = _git.Push(TicketBranch);

        Assert.True(result.IsFailure);
        Assert.Contains("no publicó la factory", result.Error.Message);
        Assert.Equal(reviewerCommit, RemoteHead(TicketBranch));
    }

    // The base pull before a re-run fetches every remote branch, so a plain
    // --force-with-lease would take its lease against the reviewer's commit and
    // drop it; the factory has to compare against what it pushed itself.
    [Fact]
    public void Push_IsRefused_WhenSomeoneElsePushedToTicketBranch_EvenOnceTheBasePullFetchedIt()
    {
        CutCommitAndPush("primer intento");
        _git.Checkout("main");
        var reviewerCommit = PushToTicketBranchFromOther("commit del revisor");

        _git.PullFastForward();
        _git.ResetBranch(TicketBranch, "main");
        CommitFile(_work, "segundo.txt", "y");
        var result = _git.Push(TicketBranch);

        Assert.True(result.IsFailure);
        Assert.Equal(reviewerCommit, RemoteHead(TicketBranch));
    }

    [Fact]
    public void Push_IsRefused_WhenRemoteBranchExistsButFactoryNeverPushedIt()
    {
        GitIn(_other, "checkout", "-b", TicketBranch);
        CommitFile(_other, "ajeno.txt", "x");
        GitIn(_other, "push", "origin", TicketBranch);
        var foreignCommit = Head(_other, TicketBranch);

        _git.ResetBranch(TicketBranch, "main");
        CommitFile(_work, "cambio.txt", "y");
        var result = _git.Push(TicketBranch);

        Assert.True(result.IsFailure);
        Assert.Equal(foreignCommit, RemoteHead(TicketBranch));
    }

    [Fact]
    public void Push_Succeeds_WhenRemoteBranchWasDeletedSinceTheFactoryPushedIt()
    {
        CutCommitAndPush("primer intento");
        _git.Checkout("main");
        GitIn(_other, "push", "origin", "--delete", TicketBranch);

        _git.ResetBranch(TicketBranch, "main");
        CommitFile(_work, "segundo.txt", "y");
        var result = _git.Push(TicketBranch);

        Assert.True(result.IsSuccess);
        Assert.Equal(Head(_work, TicketBranch), RemoteHead(TicketBranch));
    }

    [Fact]
    public void Stash_SavesModifiedAndUntrackedFiles_LeavingTreeClean()
    {
        File.WriteAllText(Path.Combine(_work, "README.md"), "modificado");
        File.WriteAllText(Path.Combine(_work, "nuevo.txt"), "untracked");

        Assert.True(_git.Stash("ne2-factory: prueba").IsSuccess);

        Assert.False(_git.HasUncommittedChanges().Value);
        Assert.Contains("ne2-factory: prueba", GitIn(_work, "stash", "list"));
    }

    [Fact]
    public void Workspace_DeliversCommittedWork_AndLeavesTreeOnBaseKeepingBranch()
    {
        var workspace = new TicketWorkspace(_git, new LocalBranchPublisher(), NullLogger<TicketWorkspace>.Instance);
        PushToMainFromOther("main avanza");

        var baseBranch = workspace.Prepare(1, onTicketBranch: true);
        Assert.Equal("main", baseBranch.Value);
        Assert.Equal(TicketBranch, CurrentBranch(_work));
        Assert.Equal(Head(_other, "main"), Head(_work, "main"));

        CommitFile(_work, "cambio.txt", "x");
        var delivered = workspace.Deliver(Ticket, "main", "resumen");
        workspace.Restore(1, "main", discardBranch: false);

        Assert.True(delivered.IsSuccess);
        Assert.Contains(TicketBranch, delivered.Value);
        Assert.Equal("main", CurrentBranch(_work));
        Assert.Contains(TicketBranch, GitIn(_work, "branch", "--list", TicketBranch));
    }

    [Fact]
    public void Workspace_RefusesDelivery_WhenSessionLeftUncommittedChanges()
    {
        var workspace = new TicketWorkspace(_git, new LocalBranchPublisher(), NullLogger<TicketWorkspace>.Instance);
        workspace.Prepare(1, onTicketBranch: true);
        CommitFile(_work, "cambio.txt", "x");
        File.WriteAllText(Path.Combine(_work, "olvidado.txt"), "x");

        var delivered = workspace.Deliver(Ticket, "main", "resumen");

        Assert.True(delivered.IsFailure);
        Assert.Contains("sin commitear", delivered.Error.Message);
    }

    [Fact]
    public void Workspace_Restore_StashesLeftoversAndDiscardsBranch_AfterBlockedRun()
    {
        var workspace = new TicketWorkspace(_git, new LocalBranchPublisher(), NullLogger<TicketWorkspace>.Instance);
        workspace.Prepare(1, onTicketBranch: true);
        CommitFile(_work, "parcial.txt", "x");
        File.WriteAllText(Path.Combine(_work, "a-medias.txt"), "x");

        workspace.Restore(1, "main", discardBranch: true);

        Assert.Equal("main", CurrentBranch(_work));
        Assert.False(_git.HasUncommittedChanges().Value);
        Assert.Contains("#1", GitIn(_work, "stash", "list"));
        Assert.Empty(GitIn(_work, "branch", "--list", TicketBranch).Trim());
        Assert.False(File.Exists(Path.Combine(_work, "parcial.txt")));
    }

    [Fact]
    public void Workspace_Prepare_Refuses_WhenTreeIsDirtyBeforeTheRun()
    {
        var workspace = new TicketWorkspace(_git, new LocalBranchPublisher(), NullLogger<TicketWorkspace>.Instance);
        File.WriteAllText(Path.Combine(_work, "del-humano.txt"), "x");

        var prepared = workspace.Prepare(1, onTicketBranch: true);

        Assert.True(prepared.IsFailure);
        Assert.Equal("main", CurrentBranch(_work));
        Assert.True(File.Exists(Path.Combine(_work, "del-humano.txt")));
    }

    private void CutCommitAndPush(string message)
    {
        _git.ResetBranch(TicketBranch, "main");
        CommitFile(_work, "intento.txt", message);
        Assert.True(_git.Push(TicketBranch).IsSuccess);
    }

    private void PushToMainFromOther(string message)
    {
        GitIn(_other, "checkout", "main");
        GitIn(_other, "pull", "--ff-only");
        CommitFile(_other, $"{Guid.NewGuid():N}.txt", message);
        GitIn(_other, "push", "origin", "main");
    }

    private string PushToTicketBranchFromOther(string message)
    {
        GitIn(_other, "fetch", "origin");
        GitIn(_other, "checkout", "-B", TicketBranch, $"origin/{TicketBranch}");
        CommitFile(_other, "revisor.txt", message);
        GitIn(_other, "push", "origin", TicketBranch);
        return Head(_other, TicketBranch);
    }

    private string CurrentBranch(string repo) => GitIn(repo, "rev-parse", "--abbrev-ref", "HEAD").Trim();

    private string Head(string repo, string branch) => GitIn(repo, "rev-parse", branch).Trim();

    private string RemoteHead(string branch) => GitIn(_remote, "rev-parse", branch).Trim();

    private void CommitFile(string repo, string file, string content)
    {
        File.WriteAllText(Path.Combine(repo, file), content);
        CommitAll(repo, content);
    }

    private void CommitAll(string repo, string message)
    {
        GitIn(repo, "add", "-A");
        GitIn(repo, "commit", "-m", message);
    }

    private void Configure(string repo)
    {
        GitIn(repo, "config", "user.name", "ne2-factory tests");
        GitIn(repo, "config", "user.email", "tests@ne2-factory.invalid");
        GitIn(repo, "config", "commit.gpgsign", "false");
    }

    private string GitIn(string dir, params string[] args)
    {
        Directory.CreateDirectory(dir);
        var (stdout, stderr, exit) = _proc.Capture("git", ["-C", dir, .. args]);
        Assert.True(exit == 0, $"git {string.Join(' ', args)} falló en {dir}: {stderr}");
        return stdout;
    }
}
