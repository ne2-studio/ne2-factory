using System.Text.Json;
using Microsoft.Extensions.Logging;
using Ne2Factory.Cli.Services;

namespace Ne2Factory.Cli.Agents;

// Only place that knows how to invoke the `claude` binary: builds its flags,
// hands off execution to IProcessRunner, and parses the JSON outcome the
// prompt instructed the agent to report as its final message.
internal sealed class ClaudeAgent(IProcessRunner proc, ILogger<ClaudeAgent> logger) : IAgent
{
    public AgentSignal? Run(string prompt, AgentOptions options)
    {
        var json = FinalResultJson(RunClaude(prompt, options));
        if (json is null) return null;

        var status = json.Value.TryGetProperty("status", out var s) ? s.GetString() : null;
        var summary = json.Value.TryGetProperty("summary", out var sum) ? sum.GetString() : null;
        var reason = json.Value.TryGetProperty("reason", out var r) ? r.GetString() : null;
        return new AgentSignal(status, summary, reason);
    }

    public RefinementSignal? RunRefinement(string prompt, AgentOptions options)
    {
        var json = FinalResultJson(RunClaude(prompt, options));
        if (json is null) return null;

        var summary = json.Value.TryGetProperty("refinement_summary", out var sum) ? sum.GetString() : null;
        var outcome = json.Value.TryGetProperty("refinement_outcome", out var o) ? o.GetString() : null;
        var questions = json.Value.TryGetProperty("questions", out var q) && q.ValueKind == JsonValueKind.Array
            ? q.EnumerateArray().Select(e => e.GetString() ?? "").ToArray()
            : null;
        return new RefinementSignal(summary, outcome, questions);
    }

    private string RunClaude(string prompt, AgentOptions options)
    {
        var args = new List<string> { "--print", "--output-format", "json" };

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

    // --output-format json wraps the session in a result envelope; its "result"
    // field is the agent's final message verbatim, which the prompt instructs
    // to be nothing but the JSON object we actually want.
    private static JsonElement? FinalResultJson(string stdout)
    {
        try
        {
            using var envelope = JsonDocument.Parse(stdout);
            var root = envelope.RootElement;
            if (root.TryGetProperty("is_error", out var isError) && isError.GetBoolean())
                return null;

            if (!root.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.String)
                return null;

            using var doc = JsonDocument.Parse(result.GetString()!);
            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
