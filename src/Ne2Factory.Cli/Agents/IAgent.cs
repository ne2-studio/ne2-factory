using System.Text.Json.Serialization;

namespace Ne2Factory.Cli.Agents;

// Abstracts spawning a headless Claude session so callers (FactoryWorker,
// GapScoutJobs, ...) don't build `claude` CLI args by hand.
public interface IAgent
{
    // Runs a headless session synchronously and returns the outcome it reported as
    // its final message, deserialized into T. The JSON schema handed to `claude
    // --json-schema` is built dynamically from T, so callers just describe the shape
    // they need instead of hand-writing (and keeping in sync) a raw schema string.
    // AgentResponse.Result is null when the session ended without a result that
    // deserializes into T (crash, manual exit, or it didn't follow the prompt's
    // reporting contract) — callers treat that as needing human review. The usage
    // metrics are filled in either way, so a failed run still reports what it cost.
    AgentResponse<T> RunWithStructuredOutput<T>(string prompt, AgentOptions options) where T : class;
}

// Outcome of one headless session plus the usage metrics `claude` reports for it
// (summed over every `claude` invocation involved, e.g. the haiku reformat fallback).
public sealed record AgentResponse<T> where T : class
{
    public T? Result { get; init; }

    public int NumTurns { get; init; }

    public int DurationMs { get; init; }

    public int DurationApiMs { get; init; }

    public double TotalCostUsd { get; init; }
}

public sealed record AgentOptions
{
    public bool SkipPermissions { get; init; }

    public string? AllowedTools { get; init; }

    // Name of a project-defined agent (agents/*.md) the session should open as,
    // via `claude --agent <name>` — the session runs under that agent's own
    // system prompt and contract from the start, instead of a generic session
    // that's told by its prompt to spawn that agent as a sub-task.
    public string? Agent { get; init; }
}

public sealed record ImplementerResponse
{
    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("summary")]
    public string? Summary { get; init; }

    [JsonPropertyName("reason")]
    public string? Reason { get; init; }
}

public sealed record RefinerResponse
{
    [JsonPropertyName("refinement_summary")]
    public string? Summary { get; init; }

    [JsonPropertyName("refinement_outcome")]
    public string? Outcome { get; init; }

    [JsonPropertyName("questions")]
    public IReadOnlyList<string>? Questions { get; init; }
}
