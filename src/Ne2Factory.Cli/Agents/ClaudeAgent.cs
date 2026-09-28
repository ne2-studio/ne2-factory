using System.Text.Json;
using System.Text.Json.Schema;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Logging;
using Ne2Factory.Cli.Services;

namespace Ne2Factory.Cli.Agents;

// Only place that knows how to invoke the `claude` binary: builds its flags
// (including a `--json-schema` computed on the fly from the caller's T), hands
// off execution to IProcessRunner, and parses the JSON outcome the prompt
// instructed the agent to report as its final message.
internal sealed class ClaudeAgent(IProcessRunner proc, ILogger<ClaudeAgent> logger) : IAgent
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    };

    public T? Run<T>(string prompt, AgentOptions options) where T : class
    {
        var jsonSchema = BuildJsonSchema<T>();
        var stdout = RunClaude(prompt, options, jsonSchema);
        return ResolveStructured<T>(stdout, jsonSchema);
    }

    private static string BuildJsonSchema<T>()
    {
        var schema = JsonSchemaExporter.GetJsonSchemaAsNode(SerializerOptions, typeof(T));
        return schema.ToJsonString();
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
    // that the result actually deserializes into T at every step rather than
    // trusting the CLI blindly:
    //   1. "structured_output" as given by the CLI.
    //   2. "result" re-parsed as JSON, if it happens to already be clean JSON.
    //   3. A small, cheap Haiku session whose only job is to reformat the raw
    //      "result" text into the required shape.
    private T? ResolveStructured<T>(string stdout, string jsonSchema) where T : class
    {
        var envelope = ParseEnvelope(stdout);
        if (envelope is not { IsError: false } e) return null;

        if (e.StructuredOutput is { } fromCli && TryDeserialize<T>(fromCli.GetRawText(), out var fromCliValue))
            return fromCliValue;

        if (e.ResultText is not { } resultText) return null;

        if (TryDeserialize<T>(resultText, out var fromResult))
        {
            logger.LogWarning("structured_output ausente o inválido; result ya era JSON válido y deserializa en {Type}, lo uso como resultado.", typeof(T).Name);
            return fromResult;
        }

        logger.LogWarning("structured_output y result no deserializan en {Type}; lanzo una sesión de reformateo con haiku.", typeof(T).Name);
        var reformatted = RunReformatFallback(resultText, jsonSchema);
        if (reformatted is { } fromFallbackText && TryDeserialize<T>(fromFallbackText, out var fromFallback))
            return fromFallback;

        return null;
    }

    // Cheap last resort: no tools, no permissions, low effort — its only job is
    // turning already-produced free text into the shape the schema demands.
    private string? RunReformatFallback(string rawText, string jsonSchema)
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
        return envelope is { IsError: false, StructuredOutput: { } so } ? so.GetRawText() : null;
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

    private static bool TryDeserialize<T>(string json, out T? value) where T : class
    {
        try
        {
            value = JsonSerializer.Deserialize<T>(json, SerializerOptions);
            return value is not null;
        }
        catch (JsonException)
        {
            value = null;
            return false;
        }
    }
}
