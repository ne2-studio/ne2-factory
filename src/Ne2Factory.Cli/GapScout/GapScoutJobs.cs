using Microsoft.Extensions.Logging;
using Ne2Factory.Cli.Agents;

namespace Ne2Factory.Cli.GapScout;

// Hangfire job body — the equivalent of what used to run inside a dedicated
// tmux window for `bin/gap-scout scan`: spawns one `claude` session running
// the architecture-gap-scout agent for a single scope. Executes inside the
// Hangfire server hosted by `ne2-factory run`, so its stdout/stderr are
// inherited straight into that same terminal. Instantiated per job by
// Hangfire's DI-backed job activator (see AddHangfire in Program.cs).
public sealed class GapScoutJobs(IAgent agent, ILogger<GapScoutJobs> logger)
{
    public const string AllowedTools = "Agent Bash(gh issue list *) Bash(gh issue create *) Bash(gh issue view *) Bash(gh label create *)";

    public void RunScan(string scope, bool yolo)
    {
        var prompt = $"Spawn the `architecture-gap-scout` agent with:\n\nScope: {scope}\n";

        logger.LogInformation("gap-scout: {Scope}", scope);
        agent.Run(prompt, new AgentOptions
        {
            SkipPermissions = yolo,
            AllowedTools = yolo ? null : AllowedTools,
        });
    }
}
