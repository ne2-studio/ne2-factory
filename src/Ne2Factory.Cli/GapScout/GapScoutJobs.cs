using Microsoft.Extensions.Logging;
using Ne2Factory.Cli.Services;

namespace Ne2Factory.Cli.GapScout;

// Hangfire job body — the equivalent of what used to run inside a dedicated
// tmux window for `bin/gap-scout scan`: spawns one `claude` session running
// the architecture-gap-scout agent for a single scope. Executes inside the
// Hangfire server hosted by `backlog run --yolo`, so its stdout/stderr are
// inherited straight into that same terminal. Instantiated per job by
// Hangfire's DI-backed job activator (see AddHangfire in Program.cs).
public sealed class GapScoutJobs(IProcessRunner proc, ILogger<GapScoutJobs> logger)
{
    public const string AllowedTools = "Agent Bash(gh issue list *) Bash(gh issue create *) Bash(gh issue view *) Bash(gh label create *)";

    public void RunScan(string scope, bool yolo)
    {
        var prompt = $"Spawn the `architecture-gap-scout` agent with:\n\nScope: {scope}\n";

        var args = new List<string>();
        if (yolo)
        {
            args.Add("--dangerously-skip-permissions");
        }
        else
        {
            args.Add("--allowed-tools");
            args.Add(AllowedTools);
        }
        args.Add(prompt);

        logger.LogInformation("gap-scout: {Scope}", scope);
        proc.RunInherited("claude", args);
    }
}
