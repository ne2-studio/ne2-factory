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
        {"type":"object","properties":{"refinement_summary":{"type":"string"},"refinement_outcome":{"type":"string","enum":["ready","missing_data"]},"questions":{"type":"array","items":{"type":"string"},"default":[]}},"required":["refinement_summary","refinement_outcome"],"additionalProperties":false}
        """;

    public AgentSignal? Run(string prompt, AgentOptions options)
    {
        var stdout = RunClaude(prompt, options, AgentSignalSchema);
        var json = ResolveStructured(stdout, AgentSignalSchema, IsValidAgentSignal);
        if (json is null) return null;

        var status = json.Value.TryGetProperty("status", out var s) ? s.GetString() : null;
        var summary = json.Value.TryGetProperty("summary", out var sum) ? sum.GetString() : null;
        var reason = json.Value.TryGetProperty("reason", out var r) ? r.GetString() : null;
        return new AgentSignal(status, summary, reason);
    }

    public RefinementSignal? RunRefinement(string prompt, AgentOptions options)
    {
        var stdout = RunClaude(prompt, options, RefinementSignalSchema);
        var json = ResolveStructured(stdout, RefinementSignalSchema, IsValidRefinementSignal);
        if (json is null) return null;

        var summary = json.Value.TryGetProperty("refinement_summary", out var sum) ? sum.GetString() : null;
        var outcome = json.Value.TryGetProperty("refinement_outcome", out var o) ? o.GetString() : null;
        var questions = json.Value.TryGetProperty("questions", out var q) && q.ValueKind == JsonValueKind.Array
            ? q.EnumerateArray().Select(e => e.GetString() ?? "").ToArray()
            : [];
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

    // --json-schema is best-effort: the CLI only fills "structured_output" when
    // the model actually invokes the schema-bound tool at the end (stop_reason
    // "tool_use"). A long/heavy session can end with a plain-text "end_turn"
    // instead, leaving "structured_output" absent even though "result" holds
    // the same answer as JSON text. So we fall back in three steps, validating
    // our own schema expectations at every step rather than trusting the CLI
    // blindly:
    //   1. "structured_output" as given by the CLI.
    //   2. "result" re-parsed as JSON, if it happens to already be clean JSON.
    //   3. A small, cheap Haiku session whose only job is to reformat the raw
    //      "result" text into the required shape.
    private JsonElement? ResolveStructured(string stdout, string jsonSchema, Func<JsonElement, bool> isValid)
    {
        var envelope = ParseEnvelope(stdout);
        if (envelope is not { IsError: false } e) return null;

        if (e.StructuredOutput is { } fromCli && isValid(fromCli))
            return fromCli;

        if (e.ResultText is not { } resultText) return null;

        if (TryParseJsonObject(resultText, out var fromResult) && isValid(fromResult))
        {
            logger.LogWarning("structured_output ausente o inválido; result ya era JSON válido y cumple el schema, lo uso como resultado.");
            return fromResult;
        }

        logger.LogWarning("structured_output y result no son interpretables como el schema esperado; lanzo una sesión de reformateo con haiku.");
        var reformatted = RunReformatFallback(resultText, jsonSchema);
        if (reformatted is { } fromFallback && isValid(fromFallback))
            return fromFallback;

        return null;
    }

    // Cheap last resort: no tools, no permissions, low effort — its only job is
    // turning already-produced free text into the shape the schema demands.
    private JsonElement? RunReformatFallback(string rawText, string jsonSchema)
    {
        var args = new List<string>
        {
            "--print", "--output-format", "json", "--json-schema", jsonSchema,
            "--model", "haiku", "--effort", "low", "--dangerously-skip-permissions",
            $"Reformatea la siguiente respuesta al formato exigido por el schema, sin añadir ni quitar información: {rawText}",
        };

        var (stdout, stderr, _) = proc.Capture("claude", args);

        logger.LogInformation("{Transcript}", stdout);
        if (!string.IsNullOrWhiteSpace(stderr))
            logger.LogWarning("{Stderr}", stderr);

        var envelope = ParseEnvelope(stdout);
        return envelope is { IsError: false, StructuredOutput: { } so } ? so : null;
    }

    private static (bool IsError, JsonElement? StructuredOutput, string? ResultText)? ParseEnvelope(string stdout)
    {
        try
        {
            using var envelope = JsonDocument.Parse(stdout);
            var root = envelope.RootElement;

            var isError = root.TryGetProperty("is_error", out var errorProp) && errorProp.GetBoolean();

            JsonElement? structuredOutput = root.TryGetProperty("structured_output", out var so) && so.ValueKind == JsonValueKind.Object
                ? so.Clone()
                : null;

            string? resultText = root.TryGetProperty("result", out var r) && r.ValueKind == JsonValueKind.String
                ? r.GetString()
                : null;

            return (isError, structuredOutput, resultText);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool TryParseJsonObject(string text, out JsonElement element)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                element = doc.RootElement.Clone();
                return true;
            }
        }
        catch (JsonException)
        {
            // fall through to the failure return below
        }

        element = default;
        return false;
    }

    private static bool IsValidAgentSignal(JsonElement e)
    {
        if (e.ValueKind != JsonValueKind.Object) return false;

        if (!e.TryGetProperty("status", out var status) || status.ValueKind != JsonValueKind.String) return false;
        if (status.GetString() is not ("done" or "blocked")) return false;

        if (!e.TryGetProperty("summary", out var summary) || summary.ValueKind != JsonValueKind.String) return false;
        if (!e.TryGetProperty("reason", out var reason) || reason.ValueKind != JsonValueKind.String) return false;

        return true;
    }

    private static bool IsValidRefinementSignal(JsonElement e)
    {
        if (e.ValueKind != JsonValueKind.Object) return false;

        if (!e.TryGetProperty("refinement_summary", out var summary) || summary.ValueKind != JsonValueKind.String) return false;

        if (!e.TryGetProperty("refinement_outcome", out var outcome) || outcome.ValueKind != JsonValueKind.String) return false;
        if (outcome.GetString() is not ("ready" or "missing_data")) return false;

        if (e.TryGetProperty("questions", out var questions) && questions.ValueKind != JsonValueKind.Array) return false;

        return true;
    }
}
