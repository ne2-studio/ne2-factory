using Microsoft.Extensions.Logging.Abstractions;
using Ne2Factory.Cli.Agents;
using Ne2Factory.Cli.Backlog;
using Ne2Factory.Cli.Common;
using Ne2Factory.Cli.FactoryWorker;
using Ne2Factory.Cli.Verification;

namespace Ne2Factory.Cli.Tests;

public class PullRequestVerificationJobsTests
{
    private const int PrNumber = 57;
    private const int IssueNumber = 42;
    private const string HeadCommit = "head0000000000000000000000000000000000000";
    private const string BaseCommit = "base0000000000000000000000000000000000000";

    private readonly InMemoryPullRequests _pullRequests = new();
    private readonly InMemoryBacklog _backlog = new();
    private readonly InMemoryAgentRunRepository _runs = new();
    private readonly FakeGit _git = new();
    private readonly FakeCodingAgent _codingAgent = new();
    private readonly PullRequestVerificationJobs _sut;

    public PullRequestVerificationJobsTests()
    {
        _git.RemoteBranches["main"] = BaseCommit;
        _git.RemoteBranches["factory/issue-42"] = HeadCommit;
        _sut = new PullRequestVerificationJobs(
            _pullRequests,
            _backlog,
            _runs,
            new VerificationWorkspace(_git, NullLogger<VerificationWorkspace>.Instance),
            _codingAgent,
            NullLogger<PullRequestVerificationJobs>.Instance);
    }

    private static PullRequest CreatePullRequest(bool isOpen = true, IReadOnlyList<string>? comments = null) =>
        new(PrNumber, "Arreglar login", "cambié X\n\nCloses #42", "https://example.com/pull/57",
            isOpen, IsDraft: false, "factory/issue-42", HeadCommit, "main", comments ?? []);

    private AgentRun AddQueuedRun()
    {
        var run = new AgentRun
        {
            Id = Guid.NewGuid(),
            IssueNumber = PrNumber,
            AgentName = PullRequestVerificationJobs.AgentName,
            Status = AgentRunStatus.Queued,
            QueuedAt = DateTime.UtcNow,
        };
        _runs.Add(run);
        return run;
    }

    private void AddTicket() =>
        _backlog.Add(new BacklogItem(IssueNumber, "Login roto", "El login falla con emails en mayúsculas", null, TicketState.InReview, ["-- comment by pedro --\nrefinado"]));

    private string LastComment() => _pullRequests.Find(PrNumber)!.Comments.Last();

    [Fact]
    public void Execute_PostsVerdictWithMarker_AndRestoresDefaultBranch()
    {
        _pullRequests.Add(CreatePullRequest());
        AddTicket();
        var run = AddQueuedRun();
        _codingAgent.OnRun = (_, _) => new VerifierResult { Status = "pass", Report = "## Verification result\n\nTodo bien" };

        _sut.Execute(run.Id);

        var comment = LastComment();
        Assert.StartsWith("## Verification: PASS", comment);
        Assert.Contains("Todo bien", comment);
        Assert.DoesNotContain("Warning", comment);
        Assert.False(PullRequestVerificationWorker.NeedsVerification(_pullRequests.Find(PrNumber)!));
        Assert.Equal("main", _git.CurrentBranch);
        var finished = _runs.Get(run.Id)!;
        Assert.Equal(AgentRunStatus.Succeeded, finished.Status);
        Assert.Equal("pass", finished.Outcome);
    }

    [Fact]
    public void Execute_RunsVerifierOnTheHeadCommit_WithTicketAsIntentAndDescriptionAsHints()
    {
        _pullRequests.Add(CreatePullRequest());
        AddTicket();
        var run = AddQueuedRun();
        string? branchDuringRun = null;
        _codingAgent.OnRun = (_, _) =>
        {
            branchDuringRun = _git.CurrentBranch;
            return new VerifierResult { Status = "PASS" };
        };

        _sut.Execute(run.Id);

        Assert.Equal(HeadCommit, branchDuringRun);
        var (prompt, options) = _codingAgent.RunCalls.Single();
        Assert.Equal("verifier", options.Agent);
        Assert.Contains("Ticket #42: Login roto", prompt);
        Assert.Contains("El login falla con emails en mayúsculas", prompt);
        Assert.Contains("refinado", prompt);
        Assert.Contains("cambié X", prompt);
        Assert.Contains(BaseCommit, prompt);
    }

    [Fact]
    public void Execute_PostsNoVerdictComment_WhenSessionHasNoInterpretableResult()
    {
        _pullRequests.Add(CreatePullRequest());
        AddTicket();
        var run = AddQueuedRun();

        _sut.Execute(run.Id);

        Assert.StartsWith("## Verification: NO VERDICT", LastComment());
        Assert.False(PullRequestVerificationWorker.NeedsVerification(_pullRequests.Find(PrNumber)!));
        Assert.Equal(AgentRunStatus.Failed, _runs.Get(run.Id)!.Status);
    }

    [Fact]
    public void Execute_WarnsAndStashes_WhenVerifierLeavesUncommittedChanges()
    {
        _pullRequests.Add(CreatePullRequest());
        AddTicket();
        var run = AddQueuedRun();
        _codingAgent.OnRun = (_, _) =>
        {
            _git.Dirty = true;
            return new VerifierResult { Status = "FAIL", Report = "falla" };
        };

        _sut.Execute(run.Id);

        Assert.Contains("**Warning:** the verifier left uncommitted changes", LastComment());
        Assert.Contains("stash", _git.Calls);
        Assert.False(_git.Dirty);
        Assert.Equal("main", _git.CurrentBranch);
    }

    [Fact]
    public void Execute_CancelsWithoutRunningVerifier_WhenHeadIsAlreadyVerified()
    {
        _pullRequests.Add(CreatePullRequest());
        AddTicket();
        var first = AddQueuedRun();
        _codingAgent.OnRun = (_, _) => new VerifierResult { Status = "PASS" };
        _sut.Execute(first.Id);

        var second = AddQueuedRun();
        _sut.Execute(second.Id);

        Assert.Single(_codingAgent.RunCalls);
        Assert.Equal(AgentRunStatus.Cancelled, _runs.Get(second.Id)!.Status);
    }

    [Fact]
    public void Execute_CancelsWithoutTouchingGit_WhenPullRequestIsClosed()
    {
        _pullRequests.Add(CreatePullRequest(isOpen: false));
        var run = AddQueuedRun();

        _sut.Execute(run.Id);

        Assert.Equal(AgentRunStatus.Cancelled, _runs.Get(run.Id)!.Status);
        Assert.Empty(_git.Calls);
        Assert.Empty(_codingAgent.RunCalls);
    }

    [Fact]
    public void Execute_FailsWithoutRunningVerifier_WhenWorkingTreeIsDirty()
    {
        _pullRequests.Add(CreatePullRequest());
        AddTicket();
        _git.Dirty = true;
        var run = AddQueuedRun();

        _sut.Execute(run.Id);

        Assert.Equal(AgentRunStatus.Failed, _runs.Get(run.Id)!.Status);
        Assert.Empty(_codingAgent.RunCalls);
        Assert.Empty(_pullRequests.Find(PrNumber)!.Comments);
    }

    [Fact]
    public void Execute_Fails_WhenCommentCannotBePosted()
    {
        _pullRequests.Add(CreatePullRequest());
        AddTicket();
        _pullRequests.CommentFailure = ApplicationError.ExternalDependencyUnavailable("gh no disponible");
        var run = AddQueuedRun();
        _codingAgent.OnRun = (_, _) => new VerifierResult { Status = "PASS" };

        _sut.Execute(run.Id);

        var finished = _runs.Get(run.Id)!;
        Assert.Equal(AgentRunStatus.Failed, finished.Status);
        Assert.Equal("gh no disponible", finished.Error);
        Assert.Equal("main", _git.CurrentBranch);
    }

    [Fact]
    public void Execute_UsesPullRequestTitleAsIntent_WhenTicketIsMissing()
    {
        _pullRequests.Add(CreatePullRequest());
        var run = AddQueuedRun();
        _codingAgent.OnRun = (_, _) => new VerifierResult { Status = "PASS" };

        _sut.Execute(run.Id);

        Assert.Contains("Arreglar login", _codingAgent.RunCalls.Single().Prompt);
        Assert.Equal(AgentRunStatus.Succeeded, _runs.Get(run.Id)!.Status);
    }

    [Theory]
    [InlineData(true, false, "factory/issue-42", true)]
    [InlineData(false, false, "factory/issue-42", false)]
    [InlineData(true, true, "factory/issue-42", false)]
    [InlineData(true, false, "feature/login", false)]
    public void NeedsVerification_OnlyForOpenReadyFactoryPullRequests(bool isOpen, bool isDraft, string headBranch, bool expected)
    {
        var pr = CreatePullRequest(isOpen) with { IsDraft = isDraft, HeadBranch = headBranch };

        Assert.Equal(expected, PullRequestVerificationWorker.NeedsVerification(pr));
    }

    [Fact]
    public void NeedsVerification_AgainAfterNewCommits()
    {
        _pullRequests.Add(CreatePullRequest());
        AddTicket();
        var run = AddQueuedRun();
        _codingAgent.OnRun = (_, _) => new VerifierResult { Status = "PASS" };
        _sut.Execute(run.Id);

        var pushedAgain = _pullRequests.Find(PrNumber)! with { HeadCommit = "new0000000000000000000000000000000000000" };

        Assert.True(PullRequestVerificationWorker.NeedsVerification(pushedAgain));
    }
}
