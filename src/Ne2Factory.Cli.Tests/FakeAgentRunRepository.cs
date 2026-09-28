using Ne2Factory.Cli.FactoryWorker;

namespace Ne2Factory.Cli.Tests;

internal sealed class FakeAgentRunRepository : IAgentRunRepository
{
    private readonly Dictionary<Guid, AgentRun> runs = [];

    public List<Guid> MarkRunningCalls { get; } = [];
    public List<(Guid Id, AgentRunStatus Status, string? Outcome, string? Error)> FinishCalls { get; } = [];

    public void Add(AgentRun run) => runs[run.Id] = run;

    public AgentRun? TryCreateQueued(int issueNumber, string agentName) =>
        throw new NotSupportedException($"{nameof(FakeAgentRunRepository)} no soporta {nameof(TryCreateQueued)}.");

    public void SetHangfireJobId(Guid id, string jobId) =>
        throw new NotSupportedException($"{nameof(FakeAgentRunRepository)} no soporta {nameof(SetHangfireJobId)}.");

    public AgentRun? Get(Guid id) => runs.GetValueOrDefault(id);

    public void MarkRunning(Guid id) => MarkRunningCalls.Add(id);

    public void Finish(Guid id, AgentRunStatus status, string? outcome, string? error) =>
        FinishCalls.Add((id, status, outcome, error));

    public IReadOnlyList<AgentRun> ListRecent(int limit) =>
        throw new NotSupportedException($"{nameof(FakeAgentRunRepository)} no soporta {nameof(ListRecent)}.");
}
