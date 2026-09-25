namespace Ne2Factory.Cli.Agents;

// Abstracts spawning a headless Claude session so callers (BacklogCommand,
// GapScoutJobs, ...) don't build `claude` CLI args by hand.
public interface IAgent
{
    int Run(string prompt, AgentOptions options);
}

public sealed record AgentOptions
{
    public bool SkipPermissions { get; init; }

    public string? AllowedTools { get; init; }
}
