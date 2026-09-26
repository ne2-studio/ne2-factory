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
        var args = new List<string> { "--print" };

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

        return ParseSignal(stdout);
    }

    public void RunInteractive(string prompt) => proc.RunInherited("claude", [prompt]);

    // The prompt tells the agent its final message must be nothing but a JSON
    // object; take the last brace-delimited chunk of stdout as that message.
    private static AgentSignal? ParseSignal(string stdout)
    {
        var start = stdout.LastIndexOf('{');
        var end = stdout.LastIndexOf('}');
        if (start < 0 || end < start) return null;

        try
        {
            using var doc = JsonDocument.Parse(stdout[start..(end + 1)]);
            var root = doc.RootElement;
            var status = root.TryGetProperty("status", out var s) ? s.GetString() : null;
            var reason = root.TryGetProperty("reason", out var r) ? r.GetString() : null;
            return new AgentSignal(status, reason);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
