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

    // Runs a session with stdio inherited from this process, so the reviewer
    // can answer prompts live (e.g. refine-ticket). Returns once the session
    // exits; callers that need to know the outcome check the backlog itself
    // (e.g. whether the ticket picked up the `refined` label) rather than
    // parsing a reported signal, since an interactive session isn't asked to
    // report one.
    void RunInteractive(string prompt);
}

public sealed record AgentOptions
{
    public bool SkipPermissions { get; init; }

    public string? AllowedTools { get; init; }
}

public sealed record AgentSignal(string? Status, string? Reason);
