using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Ne2Factory.Cli.Agents;

namespace Ne2Factory.Cli.Tests;

public class ClaudeAgentTests
{
    private static readonly AgentOptions Options = new() { SkipPermissions = true };

    private readonly FakeProcessRunner _proc = new();
    private readonly ClaudeAgent _agent;

    public ClaudeAgentTests()
    {
        _agent = new ClaudeAgent(_proc, NullLogger<ClaudeAgent>.Instance);
    }

    private void StubCapture(string primaryStdout, string? haikuStdout = null)
    {
        _proc.OnCapture = (_, args, _) =>
        {
            var isFallback = args.Contains("haiku");
            if (isFallback && haikuStdout is not null)
                return (haikuStdout, "", 0);
            return (primaryStdout, "", 0);
        };
    }

    private static string Envelope(bool isError = false, string? result = null)
    {
        var dict = new Dictionary<string, object?> { ["is_error"] = isError };
        if (result is not null) dict["result"] = result;
        return JsonSerializer.Serialize(dict);
    }

    [Fact]
    public void RunWithStructuredOutput_ReportsUsageMetrics_FromEnvelope()
    {
        var resultJson = JsonSerializer.Serialize(new { status = "done", summary = "s", reason = "r" });
        StubCapture(JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["is_error"] = false,
            ["result"] = resultJson,
            ["num_turns"] = 3,
            ["duration_ms"] = 2525,
            ["duration_api_ms"] = 3873,
            ["total_cost_usd"] = 0.058,
        }));

        var response = _agent.RunWithStructuredOutput<ImplementerResponse>("do stuff", Options);

        Assert.NotNull(response.Result);
        Assert.Equal(3, response.NumTurns);
        Assert.Equal(2525, response.DurationMs);
        Assert.Equal(3873, response.DurationApiMs);
        Assert.Equal(0.058, response.TotalCostUsd, 6);
    }

    [Fact]
    public void RunWithStructuredOutput_SumsUsageOfHaikuFallback()
    {
        string Env(string result, double cost) => JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["is_error"] = false, ["result"] = result, ["num_turns"] = 1,
            ["duration_ms"] = 100, ["duration_api_ms"] = 80, ["total_cost_usd"] = cost,
        });
        StubCapture(Env("no es json", 0.05),
            Env(JsonSerializer.Serialize(new { status = "done", summary = "s", reason = "r" }), 0.01));

        var response = _agent.RunWithStructuredOutput<ImplementerResponse>("do stuff", Options);

        Assert.NotNull(response.Result);
        Assert.Equal(2, response.NumTurns);
        Assert.Equal(200, response.DurationMs);
        Assert.Equal(160, response.DurationApiMs);
        Assert.Equal(0.06, response.TotalCostUsd, 6);
    }

    [Fact]
    public void RunWithStructuredOutput_ReturnsSignal_WhenResultIsValidJson()
    {
        var resultJson = JsonSerializer.Serialize(new { status = "done", summary = "s", reason = "r" });
        var stdout = Envelope(result: resultJson);
        StubCapture(stdout);

        var response = _agent.RunWithStructuredOutput<ImplementerResponse>("do stuff", Options).Result;

        Assert.NotNull(response);
        Assert.Equal("done", response!.Status);
        Assert.Equal("s", response.Summary);
        Assert.Equal("r", response.Reason);
        Assert.Single(_proc.CaptureCalls);
    }

    [Fact]
    public void RunWithStructuredOutput_FallsBackToHaikuReformat_WhenResultIsNotUsableJson()
    {
        var primaryStdout = Envelope(result: "La tarea salió bien, sin bloqueos ni nada raro.");
        var haikuResultJson = JsonSerializer.Serialize(new { status = "blocked", summary = "s2", reason = "r2" });
        var haikuStdout = Envelope(result: haikuResultJson);
        StubCapture(primaryStdout, haikuStdout);

        var response = _agent.RunWithStructuredOutput<ImplementerResponse>("do stuff", Options).Result;

        Assert.NotNull(response);
        Assert.Equal("blocked", response!.Status);
        Assert.Equal("s2", response.Summary);
        Assert.Equal("r2", response.Reason);
        Assert.Equal(2, _proc.CaptureCalls.Count);
        Assert.Contains(_proc.CaptureCalls, call =>
            call.Args.Contains("--model") && call.Args.Contains("haiku") &&
            call.Args.Contains("--effort") && call.Args.Contains("low"));
    }

    [Fact]
    public void RunWithStructuredOutput_ReturnsNull_WhenIsErrorTrue()
    {
        var resultJson = JsonSerializer.Serialize(new { status = "done", summary = "s", reason = "r" });
        var stdout = Envelope(isError: true, result: resultJson);
        StubCapture(stdout);

        var response = _agent.RunWithStructuredOutput<ImplementerResponse>("do stuff", Options).Result;

        Assert.Null(response);
        // is_error corta el flujo: ni siquiera se intenta el fallback de haiku.
        Assert.Single(_proc.CaptureCalls);
    }

    [Fact]
    public void RunWithStructuredOutput_ReturnsNull_WhenBothLevelsFail()
    {
        // "status" es requerido en ImplementerResponse: sin él, ningún nivel
        // deserializa y el resultado final es null.
        var primaryStdout = Envelope(result: "garbage, not json at all");
        var haikuResultJson = JsonSerializer.Serialize(new { summary = "s", reason = "r" });
        var haikuStdout = Envelope(result: haikuResultJson);
        StubCapture(primaryStdout, haikuStdout);

        var response = _agent.RunWithStructuredOutput<ImplementerResponse>("do stuff", Options).Result;

        Assert.Null(response);
        Assert.Equal(2, _proc.CaptureCalls.Count);
    }

    [Fact]
    public void RunWithStructuredOutput_ReturnsRefinerResponse_WithQuestions_WhenResultIsValidJson()
    {
        var resultJson = JsonSerializer.Serialize(new
        {
            refinement_summary = "falta info",
            refinement_outcome = "missing_data",
            questions = new[] { "¿Qué ticket?" },
        });
        var stdout = Envelope(result: resultJson);
        StubCapture(stdout);

        var response = _agent.RunWithStructuredOutput<RefinerResponse>("refine it", Options).Result;

        Assert.NotNull(response);
        Assert.Equal("falta info", response!.Summary);
        Assert.Equal("missing_data", response.Outcome);
        Assert.Equal(["¿Qué ticket?"], response.Questions);
    }

    [Fact]
    public void RunWithStructuredOutput_ReturnsNull_WhenNothingEverDeserializesIntoRequestedType()
    {
        var primaryStdout = Envelope(result: "garbage, not json at all");
        var haikuStdout = Envelope(result: "still not json");
        StubCapture(primaryStdout, haikuStdout);

        var response = _agent.RunWithStructuredOutput<RefinerResponse>("refine it", Options).Result;

        Assert.Null(response);
        Assert.Equal(2, _proc.CaptureCalls.Count);
    }

    [Fact]
    public void RunWithStructuredOutput_EmbedsJsonExampleBuiltFromT_InThePromptSentToClaude()
    {
        var resultJson = JsonSerializer.Serialize(new { status = "done", summary = "s", reason = "r" });
        StubCapture(Envelope(result: resultJson));

        _agent.RunWithStructuredOutput<ImplementerResponse>("do stuff", Options);

        var sentPrompt = Assert.Single(_proc.CaptureCalls).Args[^1];
        Assert.Contains("do stuff", sentPrompt);
        Assert.Contains(ClaudeAgent.BuildJsonExample<ImplementerResponse>(), sentPrompt);
    }

    [Fact]
    public void BuildJsonExample_UsesJsonPropertyNames_WithPlaceholderValues()
    {
        var example = ClaudeAgent.BuildJsonExample<ImplementerResponse>();

        using var doc = JsonDocument.Parse(example);
        var root = doc.RootElement;

        Assert.Equal("<status>", root.GetProperty("status").GetString());
        Assert.Equal("<summary>", root.GetProperty("summary").GetString());
        Assert.Equal("<reason>", root.GetProperty("reason").GetString());
    }

    [Fact]
    public void BuildJsonExample_TurnsArrayPropertiesIntoASingleElementPlaceholderArray()
    {
        var example = ClaudeAgent.BuildJsonExample<RefinerResponse>();

        using var doc = JsonDocument.Parse(example);
        var questions = doc.RootElement.GetProperty("questions");

        Assert.Equal(JsonValueKind.Array, questions.ValueKind);
        Assert.Equal("<questions>", Assert.Single(questions.EnumerateArray()).GetString());
    }
}
