using SQLite;

namespace Ne2Factory.Cli.FactoryWorker;

[StoreAsText]
internal enum AgentRunStatus
{
    Queued,
    Running,
    Succeeded,
    Failed,
    Cancelled,
}

// Factory's own record of "what happened", independent of Hangfire's internal
// job/state tables (which only know how to run things, not why) and of
// GitHub (which only tracks the ticket's current label state, not history).
[Table("AgentRuns")]
internal sealed class AgentRun
{
    [PrimaryKey]
    public Guid Id { get; set; }

    public int IssueNumber { get; set; }
    public string AgentName { get; set; } = "";

    public AgentRunStatus Status { get; set; }

    public string? HangfireJobId { get; set; }
    public DateTime QueuedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }

    // Raw status the agent reported ("done"/"blocked"), null if it never got that far.
    public string? Outcome { get; set; }

    // Blocked reason, cancellation reason, or exception text — whichever applies to Status.
    public string? Error { get; set; }
}

internal interface IAgentRunRepository
{
    // Inserts a Queued run for this issue, unless one is already Queued/Running
    // for it (enforced by a unique index, not a race-prone read-then-write
    // check) — returns null in that case.
    AgentRun? TryCreateQueued(int issueNumber, string agentName);

    void SetHangfireJobId(Guid id, string jobId);
    AgentRun? Get(Guid id);
    void MarkRunning(Guid id);
    void Finish(Guid id, AgentRunStatus status, string? outcome, string? error);

    // Most recent runs first, for observability (`ne2-factory runs`).
    IReadOnlyList<AgentRun> ListRecent(int limit);
}

internal sealed class SqliteAgentRunRepository : IAgentRunRepository
{
    private readonly SQLiteConnection connection;
    private readonly object gate = new();

    public SqliteAgentRunRepository(ProjectContext ctx)
    {
        connection = new SQLiteConnection(ctx.FactoryDbPath);
        connection.BusyTimeout = TimeSpan.FromSeconds(5);
        connection.CreateTable<AgentRun>();
        connection.Execute("""
            CREATE UNIQUE INDEX IF NOT EXISTS IX_AgentRuns_ActiveIssue
            ON AgentRuns (IssueNumber)
            WHERE Status IN ('Queued', 'Running')
            """);
    }

    public AgentRun? TryCreateQueued(int issueNumber, string agentName)
    {
        var run = new AgentRun
        {
            Id = Guid.NewGuid(),
            IssueNumber = issueNumber,
            AgentName = agentName,
            Status = AgentRunStatus.Queued,
            QueuedAt = DateTime.UtcNow,
        };

        lock (gate)
        {
            try
            {
                connection.Insert(run);
            }
            catch (SQLiteException ex) when (ex.Result == SQLite3.Result.Constraint)
            {
                return null;
            }
        }

        return run;
    }

    public void SetHangfireJobId(Guid id, string jobId)
    {
        lock (gate) connection.Execute("UPDATE AgentRuns SET HangfireJobId = ? WHERE Id = ?", jobId, id);
    }

    public AgentRun? Get(Guid id)
    {
        lock (gate) return connection.Find<AgentRun>(id);
    }

    public void MarkRunning(Guid id)
    {
        lock (gate)
            connection.Execute(
                "UPDATE AgentRuns SET Status = ?, StartedAt = ? WHERE Id = ?",
                nameof(AgentRunStatus.Running), DateTime.UtcNow, id);
    }

    public void Finish(Guid id, AgentRunStatus status, string? outcome, string? error)
    {
        lock (gate)
            connection.Execute(
                "UPDATE AgentRuns SET Status = ?, Outcome = ?, Error = ?, FinishedAt = ? WHERE Id = ?",
                status.ToString(), outcome, error, DateTime.UtcNow, id);
    }

    public IReadOnlyList<AgentRun> ListRecent(int limit)
    {
        lock (gate)
            return connection.Table<AgentRun>()
                .OrderByDescending(r => r.QueuedAt)
                .Take(limit)
                .ToList();
    }
}
