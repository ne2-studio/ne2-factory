using Ne2Factory.Cli.Agents;

namespace Ne2Factory.Cli.Tests;

internal sealed class FakeCodingAgent : ICodingAgent
{
    public List<(string Prompt, CodingAgentOptions Options)> RunCalls { get; } = [];

    public Func<string, CodingAgentOptions, object?>? OnRun { get; set; }

    public CodingAgentResponse<T> RunWithStructuredOutput<T>(string prompt, CodingAgentOptions options) where T : class
    {
        RunCalls.Add((prompt, options));
        return new CodingAgentResponse<T> { Result = OnRun?.Invoke(prompt, options) as T };
    }
}
