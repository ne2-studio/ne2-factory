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

    private static string Envelope(bool isError = false, object? structuredOutput = null, string? result = null)
    {
        var dict = new Dictionary<string, object?> { ["is_error"] = isError };
        if (structuredOutput is not null) dict["structured_output"] = structuredOutput;
        if (result is not null) dict["result"] = result;
        return JsonSerializer.Serialize(dict);
    }

    [Fact]
    public void Run_ReturnsSignal_WhenStructuredOutputIsValid()
    {
        var stdout = Envelope(structuredOutput: new { status = "done", summary = "s", reason = "r" });
        StubCapture(stdout);

        var response = _agent.Run<ImplementerResponse>("do stuff", Options);

        Assert.NotNull(response);
        Assert.Equal("done", response!.Status);
        Assert.Equal("s", response.Summary);
        Assert.Equal("r", response.Reason);
        Assert.Single(_proc.CaptureCalls);
    }

    [Fact]
    public void Run_FallsBackToResultField_WhenStructuredOutputIsMissingButResultIsValidJson()
    {
        var resultJson = JsonSerializer.Serialize(new { status = "done", summary = "s", reason = "r" });
        var stdout = Envelope(result: resultJson);
        StubCapture(stdout);

        var response = _agent.Run<ImplementerResponse>("do stuff", Options);

        Assert.NotNull(response);
        Assert.Equal("done", response!.Status);
        Assert.Equal("s", response.Summary);
        Assert.Equal("r", response.Reason);
        // No debe haber lanzado la sesión de reformateo: con "result" ya nos vale.
        Assert.Single(_proc.CaptureCalls);
    }

    [Fact]
    public void Run_FallsBackToHaikuReformat_WhenNeitherStructuredOutputNorResultAreUsable()
    {
        var primaryStdout = Envelope(result: "La tarea salió bien, sin bloqueos ni nada raro.");
        var haikuStdout = Envelope(structuredOutput: new { status = "blocked", summary = "s2", reason = "r2" });
        StubCapture(primaryStdout, haikuStdout);

        var response = _agent.Run<ImplementerResponse>("do stuff", Options);

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
    public void Run_ReturnsNull_WhenIsErrorTrue()
    {
        var stdout = Envelope(isError: true, structuredOutput: new { status = "done", summary = "s", reason = "r" });
        StubCapture(stdout);

        var response = _agent.Run<ImplementerResponse>("do stuff", Options);

        Assert.Null(response);
        // is_error corta el flujo: ni siquiera se intenta el fallback de haiku.
        Assert.Single(_proc.CaptureCalls);
    }

    [Fact]
    public void Run_ReturnsNull_WhenAllThreeLevelsFail()
    {
        // "status" es requerido en ImplementerResponse: sin él, ninguno de los tres niveles
        // deserializa y el resultado final es null.
        var primaryStdout = Envelope(result: "garbage, not json at all");
        var haikuStdout = Envelope(structuredOutput: new { summary = "s", reason = "r" });
        StubCapture(primaryStdout, haikuStdout);

        var response = _agent.Run<ImplementerResponse>("do stuff", Options);

        Assert.Null(response);
        Assert.Equal(2, _proc.CaptureCalls.Count);
    }

    [Fact]
    public void Run_ReturnsRefinerResponse_WithQuestions_WhenStructuredOutputIsValid()
    {
        var stdout = Envelope(structuredOutput: new
        {
            refinement_summary = "falta info",
            refinement_outcome = "missing_data",
            questions = new[] { "¿Qué ticket?" },
        });
        StubCapture(stdout);

        var response = _agent.Run<RefinerResponse>("refine it", Options);

        Assert.NotNull(response);
        Assert.Equal("falta info", response!.Summary);
        Assert.Equal("missing_data", response.Outcome);
        Assert.Equal(["¿Qué ticket?"], response.Questions);
    }

    [Fact]
    public void Run_ReturnsNull_WhenNothingEverDeserializesIntoRequestedType()
    {
        var primaryStdout = Envelope(result: "garbage, not json at all");
        var haikuStdout = Envelope(result: "still not json");
        StubCapture(primaryStdout, haikuStdout);

        var response = _agent.Run<RefinerResponse>("refine it", Options);

        Assert.Null(response);
        Assert.Equal(2, _proc.CaptureCalls.Count);
    }
}
