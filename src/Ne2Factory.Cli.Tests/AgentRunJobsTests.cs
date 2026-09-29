using Microsoft.Extensions.Logging.Abstractions;
using Ne2Factory.Cli.Agents;
using Ne2Factory.Cli.Backlog;
using Ne2Factory.Cli.Common;
using Ne2Factory.Cli.FactoryWorker;
using Ne2Factory.Cli.FactoryWorker.Publishing;

namespace Ne2Factory.Cli.Tests;

public class AgentRunJobsTests
{
    private const int IssueNumber = 42;

    private const string TicketBranch = "factory/issue-42";

    private readonly InMemoryBacklog _backlog = new();
    private readonly InMemoryAgentRunRepository _runs = new();
    private readonly FakeGit _git = new();
    private readonly FakeChangePublisher _publisher = new();
    private readonly FakeCodingAgent _codingAgent = new();
    private readonly AgentRunJobs _sut;

    public AgentRunJobsTests()
    {
        var workspace = new TicketWorkspace(_git, _publisher, NullLogger<TicketWorkspace>.Instance);
        _sut = new AgentRunJobs(
            _backlog,
            _runs,
            workspace,
            new Agent(_codingAgent, _backlog, workspace, NullLogger<Agent>.Instance),
            NullLogger<AgentRunJobs>.Instance);
    }

    private static AgentRun CreateQueuedRun(string agentName) => new()
    {
        Id = Guid.NewGuid(),
        IssueNumber = IssueNumber,
        AgentName = agentName,
        Status = AgentRunStatus.Queued,
        QueuedAt = DateTime.UtcNow,
    };

    private static BacklogItem CreateItem(TicketState state) =>
        new(IssueNumber, "Title", "Body", "https://example.com/issues/42", state, []);

    [Fact]
    public void Execute_DoesNothing_WhenRunDoesNotExist()
    {
        _sut.Execute(Guid.NewGuid());

        Assert.Null(_backlog.Get(IssueNumber));
    }

    [Fact]
    public void Execute_FinishesAsCancelled_WhenTicketStateNoLongerMatchesExpected()
    {
        var run = CreateQueuedRun("implementer");
        _runs.Add(run);
        _backlog.Add(CreateItem(TicketState.Unrefined));

        _sut.Execute(run.Id);

        var finished = _runs.Get(run.Id)!;
        Assert.NotNull(finished.StartedAt);
        Assert.Equal(AgentRunStatus.Cancelled, finished.Status);
        Assert.Null(finished.Outcome);
        Assert.NotNull(finished.Error);
        Assert.Empty(_git.Calls);
        Assert.Empty(_codingAgent.RunCalls);
    }

    [Fact]
    public void Execute_FinishesAsFailed_WhenBacklogLookupFails()
    {
        var run = CreateQueuedRun("implementer");
        _runs.Add(run);
        _backlog.GetItemFailure = ApplicationError.ExternalDependencyUnavailable("gh no disponible");

        _sut.Execute(run.Id);

        var finished = _runs.Get(run.Id)!;
        Assert.Equal(AgentRunStatus.Failed, finished.Status);
        Assert.Equal("gh no disponible", finished.Error);
        Assert.Empty(_git.Calls);
    }

    [Fact]
    public void Execute_FinishesAsFailed_WhenGitPullFails()
    {
        var run = CreateQueuedRun("implementer");
        _runs.Add(run);
        _backlog.Add(CreateItem(TicketState.Refined));
        _git.PullFailure = ApplicationError.ExternalDependencyUnavailable("git pull --ff-only falló (exit 1).");

        _sut.Execute(run.Id);

        var finished = _runs.Get(run.Id)!;
        Assert.Equal(AgentRunStatus.Failed, finished.Status);
        Assert.Equal("git pull --ff-only falló (exit 1).", finished.Error);
        Assert.Equal(TicketState.Refined, _backlog.Get(IssueNumber)!.State);
        Assert.DoesNotContain(_git.Calls, c => c.StartsWith("reset-branch"));
        Assert.Empty(_codingAgent.RunCalls);
    }

    [Fact]
    public void Execute_FinishesAsFailed_WithoutRunningImplementer_WhenWorkingTreeIsDirty()
    {
        var run = CreateQueuedRun("implementer");
        _runs.Add(run);
        _backlog.Add(CreateItem(TicketState.Refined));
        _git.Dirty = true;

        _sut.Execute(run.Id);

        var finished = _runs.Get(run.Id)!;
        Assert.Equal(AgentRunStatus.Failed, finished.Status);
        Assert.Contains("sin commitear", finished.Error);
        Assert.Equal(TicketState.Refined, _backlog.Get(IssueNumber)!.State);
        Assert.DoesNotContain("stash", _git.Calls);
        Assert.Empty(_codingAgent.RunCalls);
    }

    [Fact]
    public void Execute_RunsImplementerOnFreshTicketBranchCutFromUpdatedDefaultBranch()
    {
        var run = CreateQueuedRun("implementer");
        _runs.Add(run);
        _backlog.Add(CreateItem(TicketState.Refined));
        string? branchDuringSession = null;
        _codingAgent.OnRun = (_, _) =>
        {
            branchDuringSession = _git.CurrentBranch;
            return new ImplementerResult { Status = "done", Summary = "todo listo" };
        };

        _sut.Execute(run.Id);

        Assert.Equal(TicketBranch, branchDuringSession);
        Assert.Equal(["default-branch", "status", "checkout main", "pull", $"reset-branch {TicketBranch} main"], _git.Calls.Take(5));
    }

    [Fact]
    public void Execute_RunsRefinerOnDefaultBranchWithoutTicketBranch()
    {
        var run = CreateQueuedRun("refiner");
        _runs.Add(run);
        _backlog.Add(CreateItem(TicketState.Unrefined));
        _codingAgent.OnRun = (_, _) => new RefinerResult { Outcome = "ready", Summary = "listo" };

        _sut.Execute(run.Id);

        Assert.Equal(["default-branch", "checkout main", "pull"], _git.Calls);
    }

    [Fact]
    public void Execute_MarksBacklogFailedAndFinishesFailed_WhenImplementerReturnsNoResult()
    {
        var run = CreateQueuedRun("implementer");
        _runs.Add(run);
        _backlog.Add(CreateItem(TicketState.Refined));
        _codingAgent.OnRun = (_, _) => null;

        _sut.Execute(run.Id);

        Assert.Equal(TicketState.Failed, _backlog.Get(IssueNumber)!.State);
        var finished = _runs.Get(run.Id)!;
        Assert.Equal(AgentRunStatus.Failed, finished.Status);
        Assert.Null(finished.Outcome);
    }

    [Fact]
    public void Execute_PublishesBranchAndMarksInReview_WhenImplementerReportsDone()
    {
        var run = CreateQueuedRun("implementer");
        _runs.Add(run);
        _backlog.Add(CreateItem(TicketState.Refined));
        _codingAgent.OnRun = (_, _) => new ImplementerResult { Status = "done", Summary = "todo listo" };

        _sut.Execute(run.Id);

        var proposal = Assert.Single(_publisher.Published);
        Assert.Equal(new ChangeProposal(IssueNumber, TicketBranch, "main", "Title", "todo listo"), proposal);
        var item = _backlog.Get(IssueNumber)!;
        Assert.Equal(TicketState.InReview, item.State);
        var comment = Assert.Single(item.Comments);
        Assert.Contains("https://example.com/pull/42", comment);
        Assert.Contains("todo listo", comment);
        var finished = _runs.Get(run.Id)!;
        Assert.Equal(AgentRunStatus.Succeeded, finished.Status);
        Assert.Equal("done", finished.Outcome);
        Assert.Null(finished.Error);
        Assert.Equal("main", _git.CurrentBranch);
        Assert.Contains(TicketBranch, _git.Branches);
    }

    [Fact]
    public void Execute_FinishesFailedWithoutPublishing_WhenDoneButBranchHasNoCommits()
    {
        var run = CreateQueuedRun("implementer");
        _runs.Add(run);
        _backlog.Add(CreateItem(TicketState.Refined));
        _git.Ahead = 0;
        _codingAgent.OnRun = (_, _) => new ImplementerResult { Status = "done", Summary = "todo listo" };

        _sut.Execute(run.Id);

        Assert.Empty(_publisher.Published);
        var item = _backlog.Get(IssueNumber)!;
        Assert.Equal(TicketState.Failed, item.State);
        Assert.Contains("no tiene commits", Assert.Single(item.Comments));
        var finished = _runs.Get(run.Id)!;
        Assert.Equal(AgentRunStatus.Failed, finished.Status);
        Assert.Contains("no tiene commits", finished.Error);
        Assert.Equal("main", _git.CurrentBranch);
    }

    [Fact]
    public void Execute_FinishesFailedAndStashesLeftovers_WhenDoneButSessionLeftUncommittedChanges()
    {
        var run = CreateQueuedRun("implementer");
        _runs.Add(run);
        _backlog.Add(CreateItem(TicketState.Refined));
        _codingAgent.OnRun = (_, _) =>
        {
            _git.Dirty = true;
            return new ImplementerResult { Status = "done", Summary = "todo listo" };
        };

        _sut.Execute(run.Id);

        Assert.Empty(_publisher.Published);
        Assert.Equal(TicketState.Failed, _backlog.Get(IssueNumber)!.State);
        var finished = _runs.Get(run.Id)!;
        Assert.Equal(AgentRunStatus.Failed, finished.Status);
        Assert.Contains("sin commitear", finished.Error);
        Assert.Contains("stash", _git.Calls);
        Assert.False(_git.Dirty);
        Assert.Equal("main", _git.CurrentBranch);
    }

    [Fact]
    public void Execute_FinishesFailed_WhenDoneButPublishingFails()
    {
        var run = CreateQueuedRun("implementer");
        _runs.Add(run);
        _backlog.Add(CreateItem(TicketState.Refined));
        _codingAgent.OnRun = (_, _) => new ImplementerResult { Status = "done", Summary = "todo listo" };
        _publisher.PublishFailure = ApplicationError.ExternalDependencyUnavailable("gh pr create falló");

        _sut.Execute(run.Id);

        var item = _backlog.Get(IssueNumber)!;
        Assert.Equal(TicketState.Failed, item.State);
        Assert.Contains("gh pr create falló", Assert.Single(item.Comments));
        var finished = _runs.Get(run.Id)!;
        Assert.Equal(AgentRunStatus.Failed, finished.Status);
        Assert.Equal("gh pr create falló", finished.Error);
        Assert.Contains(TicketBranch, _git.Branches);
    }

    [Fact]
    public void Execute_FinishesFailed_WhenDoneButMarkingInReviewFails()
    {
        var run = CreateQueuedRun("implementer");
        _runs.Add(run);
        _backlog.Add(CreateItem(TicketState.Refined));
        _codingAgent.OnRun = (_, _) => new ImplementerResult { Status = "done", Summary = "todo listo" };
        _backlog.MarkInReviewFailure = ApplicationError.ExternalDependencyUnavailable("gh label falló");

        _sut.Execute(run.Id);

        Assert.Equal(TicketState.Refined, _backlog.Get(IssueNumber)!.State);
        var finished = _runs.Get(run.Id)!;
        Assert.Equal(AgentRunStatus.Failed, finished.Status);
        Assert.Equal("gh label falló", finished.Error);
        Assert.Equal("main", _git.CurrentBranch);
    }

    [Fact]
    public void Execute_MarksBacklogFailedButFinishesSuccessfully_WhenImplementerReportsBlocked()
    {
        var run = CreateQueuedRun("implementer");
        _runs.Add(run);
        _backlog.Add(CreateItem(TicketState.Refined));
        _codingAgent.OnRun = (_, _) => new ImplementerResult { Status = "blocked", Reason = "falta acceso" };

        _sut.Execute(run.Id);

        Assert.Equal(TicketState.Failed, _backlog.Get(IssueNumber)!.State);
        var finished = _runs.Get(run.Id)!;
        Assert.Equal(AgentRunStatus.Succeeded, finished.Status);
        Assert.Equal("blocked", finished.Outcome);
        Assert.Null(finished.Error);
        Assert.Empty(_publisher.Published);
        Assert.Equal("main", _git.CurrentBranch);
        Assert.DoesNotContain(TicketBranch, _git.Branches);
    }

    [Fact]
    public void Execute_MarksBacklogFailedAndFinishesFailed_WhenImplementerReturnsUnknownStatus()
    {
        var run = CreateQueuedRun("implementer");
        _runs.Add(run);
        _backlog.Add(CreateItem(TicketState.Refined));
        _codingAgent.OnRun = (_, _) => new ImplementerResult { Status = "weird" };

        _sut.Execute(run.Id);

        Assert.Equal(TicketState.Failed, _backlog.Get(IssueNumber)!.State);
        var finished = _runs.Get(run.Id)!;
        Assert.Equal(AgentRunStatus.Failed, finished.Status);
        Assert.Contains("weird", finished.Error);
    }

    [Fact]
    public void Execute_MarksBacklogFailedAndFinishesFailed_WhenRefinerReturnsNoOutcome()
    {
        var run = CreateQueuedRun("refiner");
        _runs.Add(run);
        _backlog.Add(CreateItem(TicketState.Unrefined));
        _codingAgent.OnRun = (_, _) => null;

        _sut.Execute(run.Id);

        Assert.Equal(TicketState.Failed, _backlog.Get(IssueNumber)!.State);
        var finished = _runs.Get(run.Id)!;
        Assert.Equal(AgentRunStatus.Failed, finished.Status);
        Assert.Null(finished.Outcome);
    }

    [Fact]
    public void Execute_MarksTicketRefinedAndFinishesSucceeded_WhenRefinerReportsReady()
    {
        var run = CreateQueuedRun("refiner");
        _runs.Add(run);
        _backlog.Add(CreateItem(TicketState.Unrefined));
        _codingAgent.OnRun = (_, _) => new RefinerResult { Outcome = "ready", Summary = "listo" };

        _sut.Execute(run.Id);

        var item = _backlog.Get(IssueNumber)!;
        Assert.Equal(TicketState.Refined, item.State);
        Assert.Single(item.Comments);
        var finished = _runs.Get(run.Id)!;
        Assert.Equal(AgentRunStatus.Succeeded, finished.Status);
        Assert.Equal("ready", finished.Outcome);
    }

    [Fact]
    public void Execute_MarksTicketMissingDataAndFinishesSucceeded_WhenRefinerReportsMissingData()
    {
        var run = CreateQueuedRun("refiner");
        _runs.Add(run);
        _backlog.Add(CreateItem(TicketState.Unrefined));
        _codingAgent.OnRun = (_, _) => new RefinerResult
        {
            Outcome = "missing_data",
            Summary = "faltan datos",
            Questions = ["¿Qué endpoint?"],
        };

        _sut.Execute(run.Id);

        Assert.Equal(TicketState.MissingData, _backlog.Get(IssueNumber)!.State);
        var finished = _runs.Get(run.Id)!;
        Assert.Equal(AgentRunStatus.Succeeded, finished.Status);
        Assert.Equal("missing_data", finished.Outcome);
    }

    [Fact]
    public void Execute_FinishesFailed_WhenRefinerReadyButMarkingRefinedFails()
    {
        var run = CreateQueuedRun("refiner");
        _runs.Add(run);
        _backlog.Add(CreateItem(TicketState.Unrefined));
        _codingAgent.OnRun = (_, _) => new RefinerResult { Outcome = "ready", Summary = "listo" };
        _backlog.MarkRefinedFailure = ApplicationError.ExternalDependencyUnavailable("gh label falló");

        _sut.Execute(run.Id);

        var item = _backlog.Get(IssueNumber)!;
        Assert.Equal(TicketState.Unrefined, item.State);
        Assert.Single(item.Comments);
        var finished = _runs.Get(run.Id)!;
        Assert.Equal(AgentRunStatus.Failed, finished.Status);
        Assert.Equal("gh label falló", finished.Error);
    }

    [Fact]
    public void Execute_FinishesFailed_WhenRefinerCommentFails()
    {
        var run = CreateQueuedRun("refiner");
        _runs.Add(run);
        _backlog.Add(CreateItem(TicketState.Unrefined));
        _codingAgent.OnRun = (_, _) => new RefinerResult { Outcome = "ready", Summary = "listo" };
        _backlog.CommentFailure = ApplicationError.ExternalDependencyUnavailable("gh comment falló");

        _sut.Execute(run.Id);

        var item = _backlog.Get(IssueNumber)!;
        Assert.Equal(TicketState.Unrefined, item.State);
        Assert.Empty(item.Comments);
        var finished = _runs.Get(run.Id)!;
        Assert.Equal(AgentRunStatus.Failed, finished.Status);
        Assert.Equal("gh comment falló", finished.Error);
    }
}
