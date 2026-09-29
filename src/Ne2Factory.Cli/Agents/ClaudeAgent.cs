using System.Collections;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Logging;
using Ne2Factory.Cli.Services;

namespace Ne2Factory.Cli.Agents;

// Only place that knows how to invoke the `claude` binary: builds its flags, hands off
// execution to IProcessRunner, and parses the JSON outcome the prompt instructed the
// agent to report as its final message.
internal sealed class ClaudeAgent(IProcessRunner proc, ILogger<ClaudeAgent> logger) : IAgent
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    };

    public AgentResponse<T> RunWithStructuredOutput<T>(string prompt, AgentOptions options) where T : class
    {
        var jsonExample = BuildJsonExample<T>();
        var stdout = RunClaude(WithReportingInstruction(prompt, jsonExample), options);
        return ResolveStructured<T>(stdout, jsonExample);
    }

    // What the `claude --output-format json` envelope reports about a session.
    private readonly record struct Usage(int NumTurns, int DurationMs, int DurationApiMs, double TotalCostUsd)
    {
        public static Usage operator +(Usage a, Usage b) => new(
            a.NumTurns + b.NumTurns,
            a.DurationMs + b.DurationMs,
            a.DurationApiMs + b.DurationApiMs,
            a.TotalCostUsd + b.TotalCostUsd);
    }

    private sealed record Envelope(bool IsError, string? ResultText, Usage Usage);

    // Built from T's own shape (property names, following its JsonPropertyName mapping)
    // rather than a hand-written schema, so the example the agent sees can never drift
    // from what we'll actually try to deserialize.
    internal static string BuildJsonExample<T>()
    {
        var typeInfo = SerializerOptions.GetTypeInfo(typeof(T));
        var example = new JsonObject();
        foreach (var property in typeInfo.Properties)
            example[property.Name] = ExampleValue(property.PropertyType, property.Name);
        return example.ToJsonString();
    }

    // Every leaf becomes a "<property name>" placeholder: the agent is meant to fill in
    // real content there, not copy a literal type name or made-up sample value.
    private static JsonNode? ExampleValue(Type propertyType, string propertyName)
    {
        var elementType = EnumerableElementType(propertyType);
        return elementType is not null
            ? new JsonArray(JsonValue.Create($"<{propertyName}>"))
            : JsonValue.Create($"<{propertyName}>");
    }

    private static Type? EnumerableElementType(Type type)
    {
        if (type == typeof(string)) return null;

        return type.GetInterfaces().Prepend(type)
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            .Select(i => i.GetGenericArguments()[0])
            .FirstOrDefault();
    }

    // --json-schema is unreliable: the CLI only fills "structured_output" when the
    // session closes with a tool_use turn, which long/heavy sessions routinely don't.
    // So the contract lives in the prompt itself, as an example built from T's shape,
    // and we read the agent's own final message ("result") as that JSON.
    private static string WithReportingInstruction(string prompt, string jsonExample) => $"""
        {prompt}

        ---

        Your final message must be nothing but a single JSON object shaped like this example
        (same keys, real content instead of the placeholders) — no markdown code fences, no
        prose before or after it:
        {jsonExample}
        """;

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

        args.Add("--");
        args.Add(prompt);

        var (stdout, stderr, _) = proc.Capture("claude", args);

        logger.LogInformation("{Transcript}", stdout);
        if (!string.IsNullOrWhiteSpace(stderr))
            logger.LogWarning("{Stderr}", stderr);

        return stdout;
    }

    // "result" is the agent's final message verbatim, which the prompt instructed to be
    // nothing but the JSON object we need. If the agent ignored that anyway, fall back to
    // a small, cheap Haiku session whose only job is to reformat the raw "result" text
    // into the required shape.
    private AgentResponse<T> ResolveStructured<T>(string stdout, string jsonExample) where T : class
    {
        var envelope = ParseEnvelope(stdout);
        if (envelope is not { IsError: false } e) return ToResponse<T>(null, envelope?.Usage ?? default);

        if (e.ResultText is not { } resultText) return ToResponse<T>(null, e.Usage);

        if (TryDeserialize<T>(resultText, out var fromResult))
            return ToResponse(fromResult, e.Usage);

        logger.LogWarning("result no deserializa en {Type}; lanzo una sesión de reformateo con haiku.", typeof(T).Name);
        var fallback = RunReformatFallback(resultText, jsonExample);
        var usage = e.Usage + (fallback?.Usage ?? default);
        if (fallback is { IsError: false, ResultText: { } fromFallbackText } && TryDeserialize<T>(fromFallbackText, out var fromFallback))
            return ToResponse(fromFallback, usage);

        return ToResponse<T>(null, usage);
    }

    private static AgentResponse<T> ToResponse<T>(T? result, Usage usage) where T : class => new()
    {
        Result = result,
        NumTurns = usage.NumTurns,
        DurationMs = usage.DurationMs,
        DurationApiMs = usage.DurationApiMs,
        TotalCostUsd = usage.TotalCostUsd,
    };

    // Cheap last resort: no tools, no permissions, low effort — its only job is
    // turning already-produced free text into the shape the example demands.
    private Envelope? RunReformatFallback(string rawText, string jsonExample)
    {
        var prompt = WithReportingInstruction(
            $"Reformatea la siguiente respuesta al formato exigido, sin añadir ni quitar información: {rawText}",
            jsonExample);

        var args = new List<string>
        {
            "--print", "--output-format", "json",
            "--model", "haiku", "--effort", "low", "--dangerously-skip-permissions",
            "--allowed-tools", "",
            "--",
            prompt,
        };

        var (stdout, stderr, _) = proc.Capture("claude", args);

        logger.LogInformation("{Transcript}", stdout);
        if (!string.IsNullOrWhiteSpace(stderr))
            logger.LogWarning("{Stderr}", stderr);

        return ParseEnvelope(stdout);
    }

    private static Envelope? ParseEnvelope(string stdout)
    {
        try
        {
            using var envelope = JsonDocument.Parse(stdout);
            var root = envelope.RootElement;

            var isError = root.TryGetProperty("is_error", out var errorProp) && errorProp.GetBoolean();

            string? resultText = root.TryGetProperty("result", out var r) && r.ValueKind == JsonValueKind.String
                ? r.GetString()
                : null;

            var usage = new Usage(
                root.TryGetProperty("num_turns", out var turns) && turns.TryGetInt32(out var t) ? t : 0,
                root.TryGetProperty("duration_ms", out var dur) && dur.TryGetInt32(out var d) ? d : 0,
                root.TryGetProperty("duration_api_ms", out var api) && api.TryGetInt32(out var a) ? a : 0,
                root.TryGetProperty("total_cost_usd", out var cost) && cost.TryGetDouble(out var c) ? c : 0);

            return new Envelope(isError, resultText, usage);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool TryDeserialize<T>(string json, out T? value) where T : class
    {
        try
        {
            value = JsonSerializer.Deserialize<T>(StripMarkdownFences(json), SerializerOptions);
            return value is not null;
        }
        catch (JsonException)
        {
            value = null;
            return false;
        }
    }

    // Models routinely wrap the required-to-be-bare JSON in ```json ... ``` fences despite
    // the prompt explicitly forbidding it. Strip an outer fenced block if present, and fall
    // back to the first balanced {...} object in the text otherwise.
    private static string StripMarkdownFences(string text)
    {
        var trimmed = text.Trim();

        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewline = trimmed.IndexOf('\n');
            if (firstNewline >= 0)
            {
                var closingFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
                if (closingFence > firstNewline)
                    return trimmed[(firstNewline + 1)..closingFence].Trim();
            }
        }

        var start = trimmed.IndexOf('{');
        var end = trimmed.LastIndexOf('}');
        return start >= 0 && end > start ? trimmed[start..(end + 1)] : trimmed;
    }
}
