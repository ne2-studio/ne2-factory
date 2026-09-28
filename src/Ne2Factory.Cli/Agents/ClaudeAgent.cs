using System.Text.Json;
using Microsoft.Extensions.Logging;
using Ne2Factory.Cli.Services;

namespace Ne2Factory.Cli.Agents;

// Only place that knows how to invoke the `claude` binary: builds its flags,
// hands off execution to IProcessRunner, and parses the JSON outcome the
// prompt instructed the agent to report as its final message.
internal sealed class ClaudeAgent(IProcessRunner proc, ILogger<ClaudeAgent> logger) : IAgent
{
    // Passed to `claude --json-schema` so the CLI itself validates the final
    // message against the shape we need, instead of us trusting the prompt's
    // wording and hoping the agent didn't wrap it in prose or a code fence.
    private const string AgentSignalSchema = """
        {"type":"object","properties":{"status":{"type":"string","enum":["done","blocked"]},"summary":{"type":"string"},"reason":{"type":"string"}},"required":["status","summary","reason"],"additionalProperties":false}
        """;

    private const string RefinementSignalSchema = """
        {"type":"object","properties":{"refinement_summary":{"type":"string"},"refinement_outcome":{"type":"string","enum":["ready","missing_data"]},"questions":{"type":"array","items":{"type":"string"}}},"required":["refinement_summary","refinement_outcome","questions"],"additionalProperties":false}
        """;

    public AgentSignal? Run(string prompt, AgentOptions options)
    {
        var json = FinalResultJson(RunClaude(prompt, options, AgentSignalSchema));
        if (json is null) return null;

        var status = json.Value.TryGetProperty("status", out var s) ? s.GetString() : null;
        var summary = json.Value.TryGetProperty("summary", out var sum) ? sum.GetString() : null;
        var reason = json.Value.TryGetProperty("reason", out var r) ? r.GetString() : null;
        return new AgentSignal(status, summary, reason);
    }

    public RefinementSignal? RunRefinement(string prompt, AgentOptions options)
    {
        var json = FinalResultJson(RunClaude(prompt, options, RefinementSignalSchema));
        if (json is null) return null;

        var summary = json.Value.TryGetProperty("refinement_summary", out var sum) ? sum.GetString() : null;
        var outcome = json.Value.TryGetProperty("refinement_outcome", out var o) ? o.GetString() : null;
        var questions = json.Value.TryGetProperty("questions", out var q) && q.ValueKind == JsonValueKind.Array
            ? q.EnumerateArray().Select(e => e.GetString() ?? "").ToArray()
            : null;
        return new RefinementSignal(summary, outcome, questions);
    }

    private string RunClaude(string prompt, AgentOptions options, string jsonSchema)
    {
        var args = new List<string> { "--print", "--output-format", "json", "--json-schema", jsonSchema };

        if (options.Agent is not null)
        {
            args.Add("--agent");
            args.Add(options.Agent);
        }

        if (options.SkipPermissions)
            args.Add("--dangerously-skip-permissions");
        else if (options.AllowedTools is not null)
        {
            args.Add("--allowed-tools");
            args.Add(options.AllowedTools);
        }

        args.Add(prompt);

        var (stdout, stderr, _) = proc.Capture("claude", args);

        logger.LogInformation("{Transcript}", stdout);
        if (!string.IsNullOrWhiteSpace(stderr))
            logger.LogWarning("{Stderr}", stderr);

        return stdout;
    }

    // --output-format json wraps the session in a result envelope. Because we
    // pass --json-schema, the CLI itself validates the agent's final message
    // against our schema and echoes the parsed result as "structured_output" —
    // so we read that directly instead of re-parsing "result" as JSON text,
    // which used to break whenever the agent wrapped it in prose or a fence.
    private static JsonElement? FinalResultJson(string stdout)
    {
        try
        {
            using var envelope = JsonDocument.Parse(stdout);
            var root = envelope.RootElement;
            if (root.TryGetProperty("is_error", out var isError) && isError.GetBoolean())
                return null;

            if (!root.TryGetProperty("structured_output", out var result) || result.ValueKind != JsonValueKind.Object)
                return null;

            return result.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
