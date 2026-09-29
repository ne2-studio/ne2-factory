using Microsoft.Extensions.Logging;
using Ne2Factory.Cli.Agents;
using Ne2Factory.Cli.Services;

namespace Ne2Factory.Cli.Tests;

// Prueba de regresión manual: golpea el binario `claude` real (coste y latencia reales,
// requiere estar autenticado en esta máquina). No la lanza `dotnet test` a secas —
// está marcada con Category=Manual para poder excluirla del resto de la suite. Se
// ejecuta a propósito con:
//   dotnet test --filter "Category=Manual"
[Trait("Category", "Manual")]
public class ManualClaudeAgentRealCliTests
{
    private readonly ClaudeCodingAgent _codingAgent = new(
        new ProcessRunner(LoggerFactory.Create(b => b.AddConsole()).CreateLogger<ProcessRunner>()),
        LoggerFactory.Create(b => b.AddConsole()).CreateLogger<ClaudeCodingAgent>());

    [Fact]
    public void RunWithStructuredOutput_ImplementerResponse_AgainstRealClaudeCli()
    {
        var response = _codingAgent.RunWithStructuredOutput<ImplementerResult>(
            "Esto es una prueba de humo. No hagas nada más que reportar tu resultado: status done, summary 'prueba de humo ok', reason vacío.",
            new CodingAgentOptions { SkipPermissions = true }).Result;

        Assert.NotNull(response);
        Assert.Equal("done", response!.Status);
        Assert.False(string.IsNullOrWhiteSpace(response.Summary));
    }

    [Fact]
    public void RunWithStructuredOutput_RefinerResponse_AgainstRealClaudeCli()
    {
        var response = _codingAgent.RunWithStructuredOutput<RefinerResult>(
            "Esto es una prueba de humo. No hagas nada más que reportar tu resultado: refinement_outcome ready, " +
            "refinement_summary 'prueba de humo ok', sin questions.",
            new CodingAgentOptions { SkipPermissions = true }).Result;

        Assert.NotNull(response);
        Assert.Equal("ready", response!.Outcome);
        Assert.False(string.IsNullOrWhiteSpace(response.Summary));
    }
}
