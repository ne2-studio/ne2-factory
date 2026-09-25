namespace Ne2Factory.Cli.Agents;

public sealed record AgentSignal(string? Status, string? Reason);

// Out-of-band IPC with a headless agent session: it has no stdout/stdin to
// talk back on (--print just streams its own transcript), so skills like
// work-ticket report their outcome by writing a signal instead.
public interface IAgentSignalChannel
{
    // Clears any previous signal so a stale one from an earlier run can't be
    // mistaken for this one's outcome. Call before starting the agent.
    void Reset();

    // Null means the agent session ended without ever writing a signal
    // (manual exit, crash, timeout). Consumes (deletes) the signal once read.
    AgentSignal? Read();
}
