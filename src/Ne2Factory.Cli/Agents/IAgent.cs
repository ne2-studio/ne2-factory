namespace Ne2Factory.Cli.Agents;

// Abstracts spawning a headless Claude session so callers (BacklogQueueProcessor,
// GapScoutJobs, ...) don't build `claude` CLI args by hand.
public interface IAgent
{
    // Runs a headless session synchronously and returns the outcome it reported
    // as its final message. Null means the session ended without a parseable
    // outcome (crash, manual exit, or it didn't follow the prompt's reporting
    // contract) — callers treat that as needing human review.
    AgentSignal? Run(string prompt, AgentOptions options);
}

public sealed record AgentOptions
{
    public bool SkipPermissions { get; init; }

    public string? AllowedTools { get; init; }
}

public sealed record AgentSignal(string? Status, string? Reason);
