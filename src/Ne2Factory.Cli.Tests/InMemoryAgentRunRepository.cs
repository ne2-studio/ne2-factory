using Ne2Factory.Cli.FactoryWorker;

namespace Ne2Factory.Cli.Tests;

internal sealed class InMemoryAgentRunRepository : IAgentRunRepository
{
    private readonly Dictionary<Guid, AgentRun> runs = [];

    public void Add(AgentRun run) => runs[run.Id] = run;

    public AgentRun? TryCreateQueued(int issueNumber, string agentName) =>
        throw new NotSupportedException($"{nameof(InMemoryAgentRunRepository)} no soporta {nameof(TryCreateQueued)}.");

    public void SetHangfireJobId(Guid id, string jobId) =>
        throw new NotSupportedException($"{nameof(InMemoryAgentRunRepository)} no soporta {nameof(SetHangfireJobId)}.");

    public AgentRun? Get(Guid id) => runs.GetValueOrDefault(id);

    public void MarkRunning(Guid id)
    {
        var run = runs[id];
        run.Status = AgentRunStatus.Running;
        run.StartedAt = DateTime.UtcNow;
    }

    public void Finish(Guid id, AgentRunStatus status, string? outcome, string? error)
    {
        var run = runs[id];
        run.Status = status;
        run.Outcome = outcome;
        run.Error = error;
        run.FinishedAt = DateTime.UtcNow;
    }

    public IReadOnlyList<AgentRun> ListRecent(int limit) =>
        throw new NotSupportedException($"{nameof(InMemoryAgentRunRepository)} no soporta {nameof(ListRecent)}.");
}
