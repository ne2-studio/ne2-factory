using Microsoft.Extensions.Logging.Abstractions;
using Ne2Factory.Cli.Agents;
using Ne2Factory.Cli.Backlog;
using Ne2Factory.Cli.Common;
using Ne2Factory.Cli.FactoryWorker;

namespace Ne2Factory.Cli.Tests;

public class AgentRunJobsTests
{
    private const int IssueNumber = 42;

    private readonly FakeBacklog _backlog = new();
    private readonly InMemoryAgentRunRepository _runs = new();
    private readonly FakeProcessRunner _proc = new();
    private readonly FakeAgent _agent = new();
    private readonly AgentRunJobs _sut;

    public AgentRunJobsTests()
    {
        _sut = new AgentRunJobs(_backlog, _runs, _proc, _agent, NullLogger<AgentRunJobs>.Instance);
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

        Assert.Empty(_backlog.GetItemCalls);
    }

    [Fact]
    public void Execute_FinishesAsCancelled_WhenTicketStateNoLongerMatchesExpected()
    {
        var run = CreateQueuedRun("implementer");
        _runs.Add(run);
        _backlog.GetItemResult = Result.Success<BacklogItem?>(CreateItem(TicketState.Unrefined));

        _sut.Execute(run.Id);

        var finished = _runs.Get(run.Id)!;
        Assert.NotNull(finished.StartedAt);
        Assert.Equal(AgentRunStatus.Cancelled, finished.Status);
        Assert.Null(finished.Outcome);
        Assert.NotNull(finished.Error);
        Assert.Empty(_proc.RunInheritedCalls);
        Assert.Empty(_agent.RunCalls);
    }

    [Fact]
    public void Execute_FinishesAsFailed_WhenBacklogLookupFails()
    {
        var run = CreateQueuedRun("implementer");
        _runs.Add(run);
        _backlog.GetItemResult = Result.Failure<BacklogItem?>(ApplicationError.ExternalDependencyUnavailable("gh no disponible"));

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
        _backlog.GetItemResult = Result.Success<BacklogItem?>(CreateItem(TicketState.Refined));
        _proc.OnRunInherited = (_, _, _) => 1;

        _sut.Execute(run.Id);

        var finished = _runs.Get(run.Id)!;
        Assert.Equal(AgentRunStatus.Failed, finished.Status);
        Assert.Contains("1", finished.Error);
        Assert.Empty(_agent.RunCalls);
    }

    [Fact]
    public void Execute_MarksBacklogFailedAndFinishesFailed_WhenImplementerReturnsNoResult()
    {
        var run = CreateQueuedRun("implementer");
        _runs.Add(run);
        _backlog.GetItemResult = Result.Success<BacklogItem?>(CreateItem(TicketState.Refined));
        _agent.OnRun = (_, _) => null;

        _sut.Execute(run.Id);

        Assert.Equal([IssueNumber], _backlog.MarkFailedNumbers);
        var finished = _runs.Get(run.Id)!;
        Assert.Equal(AgentRunStatus.Failed, finished.Status);
        Assert.Null(finished.Outcome);
    }

    [Fact]
    public void Execute_ClosesIssueAndFinishesSucceeded_WhenImplementerReportsDone()
    {
        var run = CreateQueuedRun("implementer");
        _runs.Add(run);
        _backlog.GetItemResult = Result.Success<BacklogItem?>(CreateItem(TicketState.Refined));
        _agent.OnRun = (_, _) => new ImplementerResponse { Status = "done", Summary = "todo listo" };

        _sut.Execute(run.Id);

        Assert.Equal([IssueNumber], _backlog.ClosedNumbers);
        Assert.Single(_backlog.Comments);
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
        _backlog.GetItemResult = Result.Success<BacklogItem?>(CreateItem(TicketState.Refined));
        _agent.OnRun = (_, _) => new ImplementerResponse { Status = "done", Summary = "todo listo" };
        _backlog.CloseResult = Result.Failure(ApplicationError.ExternalDependencyUnavailable("gh close falló"));

        _sut.Execute(run.Id);

        var finished = _runs.Get(run.Id)!;
        Assert.Equal(AgentRunStatus.Failed, finished.Status);
        Assert.Equal("gh close falló", finished.Error);
    }

    [Fact]
    public void Execute_MarksBacklogFailedAndFinishesFailed_WhenImplementerReportsBlocked()
    {
        var run = CreateQueuedRun("implementer");
        _runs.Add(run);
        _backlog.GetItemResult = Result.Success<BacklogItem?>(CreateItem(TicketState.Refined));
        _agent.OnRun = (_, _) => new ImplementerResponse { Status = "blocked", Reason = "falta acceso" };

        _sut.Execute(run.Id);

        Assert.Equal([IssueNumber], _backlog.MarkFailedNumbers);
        var finished = _runs.Get(run.Id)!;
        Assert.Equal(AgentRunStatus.Failed, finished.Status);
        Assert.Equal("blocked", finished.Outcome);
        Assert.Equal("falta acceso", finished.Error);
    }

    [Fact]
    public void Execute_MarksBacklogFailedAndFinishesFailed_WhenImplementerReturnsUnknownStatus()
    {
        var run = CreateQueuedRun("implementer");
        _runs.Add(run);
        _backlog.GetItemResult = Result.Success<BacklogItem?>(CreateItem(TicketState.Refined));
        _agent.OnRun = (_, _) => new ImplementerResponse { Status = "weird" };

        _sut.Execute(run.Id);

        Assert.Equal([IssueNumber], _backlog.MarkFailedNumbers);
        var finished = _runs.Get(run.Id)!;
        Assert.Equal(AgentRunStatus.Failed, finished.Status);
        Assert.Contains("weird", finished.Error);
    }

    [Fact]
    public void Execute_MarksBacklogFailedAndFinishesFailed_WhenRefinerReturnsNoOutcome()
    {
        var run = CreateQueuedRun("refiner");
        _runs.Add(run);
        _backlog.GetItemResult = Result.Success<BacklogItem?>(CreateItem(TicketState.Unrefined));
        _agent.OnRun = (_, _) => null;

        _sut.Execute(run.Id);

        Assert.Equal([IssueNumber], _backlog.MarkFailedNumbers);
        var finished = _runs.Get(run.Id)!;
        Assert.Equal(AgentRunStatus.Failed, finished.Status);
        Assert.Null(finished.Outcome);
    }

    [Fact]
    public void Execute_MarksTicketRefinedAndFinishesSucceeded_WhenRefinerReportsReady()
    {
        var run = CreateQueuedRun("refiner");
        _runs.Add(run);
        _backlog.GetItemResult = Result.Success<BacklogItem?>(CreateItem(TicketState.Unrefined));
        _agent.OnRun = (_, _) => new RefinerResponse { Outcome = "ready", Summary = "listo" };

        _sut.Execute(run.Id);

        Assert.Equal([IssueNumber], _backlog.MarkRefinedNumbers);
        Assert.Empty(_backlog.MarkMissingDataNumbers);
        var finished = _runs.Get(run.Id)!;
        Assert.Equal(AgentRunStatus.Succeeded, finished.Status);
        Assert.Equal("ready", finished.Outcome);
    }

    [Fact]
    public void Execute_MarksTicketMissingDataAndFinishesSucceeded_WhenRefinerReportsMissingData()
    {
        var run = CreateQueuedRun("refiner");
        _runs.Add(run);
        _backlog.GetItemResult = Result.Success<BacklogItem?>(CreateItem(TicketState.Unrefined));
        _agent.OnRun = (_, _) => new RefinerResponse
        {
            Outcome = "missing_data",
            Summary = "faltan datos",
            Questions = ["¿Qué endpoint?"],
        };

        _sut.Execute(run.Id);

        Assert.Equal([IssueNumber], _backlog.MarkMissingDataNumbers);
        Assert.Empty(_backlog.MarkRefinedNumbers);
        var finished = _runs.Get(run.Id)!;
        Assert.Equal(AgentRunStatus.Succeeded, finished.Status);
        Assert.Equal("missing_data", finished.Outcome);
    }

    [Fact]
    public void Execute_FinishesFailed_WhenRefinerReadyButMarkingRefinedFails()
    {
        var run = CreateQueuedRun("refiner");
        _runs.Add(run);
        _backlog.GetItemResult = Result.Success<BacklogItem?>(CreateItem(TicketState.Unrefined));
        _agent.OnRun = (_, _) => new RefinerResponse { Outcome = "ready", Summary = "listo" };
        _backlog.MarkRefinedResult = Result.Failure(ApplicationError.ExternalDependencyUnavailable("gh label falló"));

        _sut.Execute(run.Id);

        var finished = _runs.Get(run.Id)!;
        Assert.Equal(AgentRunStatus.Failed, finished.Status);
        Assert.Equal("gh label falló", finished.Error);
    }

    [Fact]
    public void Execute_FinishesFailed_WhenRefinerCommentFails()
    {
        var run = CreateQueuedRun("refiner");
        _runs.Add(run);
        _backlog.GetItemResult = Result.Success<BacklogItem?>(CreateItem(TicketState.Unrefined));
        _agent.OnRun = (_, _) => new RefinerResponse { Outcome = "ready", Summary = "listo" };
        _backlog.CommentResult = Result.Failure(ApplicationError.ExternalDependencyUnavailable("gh comment falló"));

        _sut.Execute(run.Id);

        Assert.Empty(_backlog.MarkRefinedNumbers);
        var finished = _runs.Get(run.Id)!;
        Assert.Equal(AgentRunStatus.Failed, finished.Status);
        Assert.Equal("gh comment falló", finished.Error);
    }
}
