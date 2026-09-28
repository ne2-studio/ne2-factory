using Ne2Factory.Cli.Agents;

namespace Ne2Factory.Cli.Tests;

internal sealed class FakeAgent : IAgent
{
    public List<(string Prompt, AgentOptions Options)> RunCalls { get; } = [];

    public Func<string, AgentOptions, object?>? OnRun { get; set; }

    public T? Run<T>(string prompt, AgentOptions options) where T : class
    {
        RunCalls.Add((prompt, options));
        return OnRun?.Invoke(prompt, options) as T;
    }
}
