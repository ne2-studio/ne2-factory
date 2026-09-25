using Ne2Factory.Cli.Services;

namespace Ne2Factory.Cli.Agents;

// Only place that knows how to invoke the `claude` binary: builds its flags
// and hands off execution to IProcessRunner.
internal sealed class ClaudeAgent(IProcessRunner proc) : IAgent
{
    public int Run(string prompt, AgentOptions options)
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
        return proc.RunInherited("claude", args);
    }
}
