using Microsoft.Extensions.Logging.Abstractions;
using Ne2Factory.Cli.Agents;
using Ne2Factory.Cli.Backlog;
using Ne2Factory.Cli.Common;
using Ne2Factory.Cli.FactoryWorker;

namespace Ne2Factory.Cli.Tests;

public class AgentRunJobsTests
{
    private const int IssueNumber = 42;

    private readonly InMemoryBacklog _backlog = new();
    private readonly InMemoryAgentRunRepository _runs = new();
    private readonly FakeProcessRunner _proc = new();
    private readonly FakeCodingAgent _codingAgent = new();
    private readonly AgentRunJobs _sut;

    public AgentRunJobsTests()
    {
        _sut = new AgentRunJobs(
            _backlog,
            _runs,
            _proc,
            new Agent(_codingAgent, _backlog, NullLogger<Agent>.Instance),
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
        Assert.Empty(_proc.RunInheritedCalls);
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
        Assert.Empty(_proc.RunInheritedCalls);
    }

    [Fact]
    public void Execute_FinishesAsFailed_WhenGitPullFails()
    {
        var run = CreateQueuedRun("implementer");
        _runs.Add(run);
        _backlog.Add(CreateItem(TicketState.Refined));
        _proc.OnRunInherited = (_, _, _) => 1;

        _sut.Execute(run.Id);

        var finished = _runs.Get(run.Id)!;
        Assert.Equal(AgentRunStatus.Failed, finished.Status);
        Assert.Contains("1", finished.Error);
        Assert.Empty(_codingAgent.RunCalls);
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
    public void Execute_ClosesIssueAndFinishesSucceeded_WhenImplementerReportsDone()
    {
        var run = CreateQueuedRun("implementer");
        _runs.Add(run);
        _backlog.Add(CreateItem(TicketState.Refined));
        _codingAgent.OnRun = (_, _) => new ImplementerResult { Status = "done", Summary = "todo listo" };

        _sut.Execute(run.Id);

        var item = _backlog.Get(IssueNumber)!;
        Assert.Equal(TicketState.Done, item.State);
        Assert.Single(item.Comments);
        var finished = _runs.Get(run.Id)!;
        Assert.Equal(AgentRunStatus.Succeeded, finished.Status);
        Assert.Equal("done", finished.Outcome);
        Assert.Null(finished.Error);
    }

    [Fact]
    public void Execute_FinishesFailed_WhenDoneButClosingIssueFails()
    {
        var run = CreateQueuedRun("implementer");
        _runs.Add(run);
        _backlog.Add(CreateItem(TicketState.Refined));
        _codingAgent.OnRun = (_, _) => new ImplementerResult { Status = "done", Summary = "todo listo" };
        _backlog.CloseFailure = ApplicationError.ExternalDependencyUnavailable("gh close falló");

        _sut.Execute(run.Id);

        Assert.Equal(TicketState.Refined, _backlog.Get(IssueNumber)!.State);
        var finished = _runs.Get(run.Id)!;
        Assert.Equal(AgentRunStatus.Failed, finished.Status);
        Assert.Equal("gh close falló", finished.Error);
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
