using Microsoft.Extensions.Logging;

namespace Ne2Factory.Cli.FactoryWorker;

// Read-only observability over AgentRuns (.ne2-factory/database.db): what the
// worker has run, regardless of which IBacklog backend filed the ticket.
internal sealed class RunsCommand(IAgentRunRepository runs, ILogger<RunsCommand> logger)
{
    private const int DefaultLimit = 20;

    private const string Usage = """
        Usage: ne2-factory runs [--limit <n>]   (from the root of the repo being worked on)

        Lists the most recent AgentRuns recorded in the factory's database
        (.ne2-factory/database.db), most recent first — one row per ticket
        execution attempt, independent of the backlog backend (GitHub issues
        or local files) that filed the ticket. Useful to see what the worker
        has done: which tickets ran, when, and how they ended
        (Succeeded/Failed/Cancelled), with the agent's raw outcome/error.

        --limit <n>   Show at most n runs (default 20).
        """;

    public int Run(string[] args)
    {
        if (args is ["-h"] or ["--help"])
        {
            Console.WriteLine(Usage);
            return 0;
        }

        var limit = DefaultLimit;
        if (args is ["--limit", var n])
        {
            if (!int.TryParse(n, out limit) || limit <= 0)
            {
                logger.LogError("Uso: ne2-factory runs [--limit <n>]");
                return 1;
            }
        }
        else if (args.Length > 0)
        {
            logger.LogError("Uso: ne2-factory runs [--limit <n>]");
            return 1;
        }

        var items = runs.ListRecent(limit);
        if (items.Count == 0)
        {
            Console.WriteLine("Sin runs registradas todavía.");
            return 0;
        }

        foreach (var run in items)
        {
            Console.WriteLine(
                $"#{run.IssueNumber} {run.Status} agente={run.AgentName} encolada={run.QueuedAt:u} inicio={run.StartedAt:u} fin={run.FinishedAt:u} outcome={run.Outcome ?? "-"} error={run.Error ?? "-"} run={run.Id}");
        }

        return 0;
    }
}
