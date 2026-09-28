using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Ne2Factory.Cli.Agents;
using Ne2Factory.Cli.Services;
using NSubstitute;

namespace Ne2Factory.Cli.Tests;

public class ClaudeAgentTests
{
    private static readonly AgentOptions Options = new() { SkipPermissions = true };

    private readonly IProcessRunner _proc = Substitute.For<IProcessRunner>();
    private readonly ClaudeAgent _agent;

    public ClaudeAgentTests()
    {
        _agent = new ClaudeAgent(_proc, NullLogger<ClaudeAgent>.Instance);
    }

    private void StubCapture(string primaryStdout, string? haikuStdout = null)
    {
        _proc.Capture(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<string?>())
            .Returns(callInfo =>
            {
                var args = callInfo.ArgAt<IReadOnlyList<string>>(1);
                var isFallback = args.Contains("haiku");
                if (isFallback && haikuStdout is not null)
                    return (haikuStdout, "", 0);
                return (primaryStdout, "", 0);
            });
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

        var signal = _agent.Run<AgentSignal>("do stuff", Options);

        Assert.NotNull(signal);
        Assert.Equal("done", signal!.Status);
        Assert.Equal("s", signal.Summary);
        Assert.Equal("r", signal.Reason);
        _proc.Received(1).Capture(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<string?>());
    }

    [Fact]
    public void Run_FallsBackToResultField_WhenStructuredOutputIsMissingButResultIsValidJson()
    {
        var resultJson = JsonSerializer.Serialize(new { status = "done", summary = "s", reason = "r" });
        var stdout = Envelope(result: resultJson);
        StubCapture(stdout);

        var signal = _agent.Run<AgentSignal>("do stuff", Options);

        Assert.NotNull(signal);
        Assert.Equal("done", signal!.Status);
        Assert.Equal("s", signal.Summary);
        Assert.Equal("r", signal.Reason);
        // No debe haber lanzado la sesión de reformateo: con "result" ya nos vale.
        _proc.Received(1).Capture(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<string?>());
    }

    [Fact]
    public void Run_FallsBackToHaikuReformat_WhenNeitherStructuredOutputNorResultAreUsable()
    {
        var primaryStdout = Envelope(result: "La tarea salió bien, sin bloqueos ni nada raro.");
        var haikuStdout = Envelope(structuredOutput: new { status = "blocked", summary = "s2", reason = "r2" });
        StubCapture(primaryStdout, haikuStdout);

        var signal = _agent.Run<AgentSignal>("do stuff", Options);

        Assert.NotNull(signal);
        Assert.Equal("blocked", signal!.Status);
        Assert.Equal("s2", signal.Summary);
        Assert.Equal("r2", signal.Reason);
        _proc.Received(2).Capture(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<string?>());
        _proc.Received(1).Capture(
            Arg.Any<string>(),
            Arg.Is<IReadOnlyList<string>>(a => a.Contains("--model") && a.Contains("haiku") && a.Contains("--effort") && a.Contains("low")),
            Arg.Any<string?>());
    }

    [Fact]
    public void Run_ReturnsNull_WhenIsErrorTrue()
    {
        var stdout = Envelope(isError: true, structuredOutput: new { status = "done", summary = "s", reason = "r" });
        StubCapture(stdout);

        var signal = _agent.Run<AgentSignal>("do stuff", Options);

        Assert.Null(signal);
        // is_error corta el flujo: ni siquiera se intenta el fallback de haiku.
        _proc.Received(1).Capture(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<string?>());
    }

    [Fact]
    public void Run_ReturnsNull_WhenAllThreeLevelsFail()
    {
        // "status" es requerido en AgentSignal: sin él, ninguno de los tres niveles
        // deserializa y el resultado final es null.
        var primaryStdout = Envelope(result: "garbage, not json at all");
        var haikuStdout = Envelope(structuredOutput: new { summary = "s", reason = "r" });
        StubCapture(primaryStdout, haikuStdout);

        var signal = _agent.Run<AgentSignal>("do stuff", Options);

        Assert.Null(signal);
        _proc.Received(2).Capture(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<string?>());
    }

    [Fact]
    public void Run_ReturnsRefinementSignal_WithQuestions_WhenStructuredOutputIsValid()
    {
        var stdout = Envelope(structuredOutput: new
        {
            refinement_summary = "falta info",
            refinement_outcome = "missing_data",
            questions = new[] { "¿Qué ticket?" },
        });
        StubCapture(stdout);

        var signal = _agent.Run<RefinementSignal>("refine it", Options);

        Assert.NotNull(signal);
        Assert.Equal("falta info", signal!.Summary);
        Assert.Equal("missing_data", signal.Outcome);
        Assert.Equal(["¿Qué ticket?"], signal.Questions);
    }

    [Fact]
    public void Run_ReturnsNull_WhenNothingEverDeserializesIntoRequestedType()
    {
        var primaryStdout = Envelope(result: "garbage, not json at all");
        var haikuStdout = Envelope(result: "still not json");
        StubCapture(primaryStdout, haikuStdout);

        var signal = _agent.Run<RefinementSignal>("refine it", Options);

        Assert.Null(signal);
        _proc.Received(2).Capture(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<string?>());
    }
}
