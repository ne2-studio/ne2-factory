using Microsoft.Extensions.Logging;
using Ne2Factory.Cli.Agents;

namespace Ne2Factory.Cli.Backlog;

internal sealed class BacklogCommand(IBacklog backlog, IAgent agent, ILogger<BacklogCommand> logger)
{
    private const string Usage = """
        Usage: ne2-factory backlog <command>   (from the root of the repo being worked on)

        Commands:
          list               List queued (refined/unrefined split)/done/failed tickets.
          refine [--all]     Refine the next unrefined ticket interactively (or, with
                                --all, every unrefined ticket in sequence). See below.
          requeue <number>   Move a failed/blocked ticket back into the queue
                                (unrefined).

        `list` and `requeue` are purely inspection/bookkeeping — they never run
        tickets. `refine` hands the terminal to an interactive `claude` session per
        ticket, so it can ask the reviewer questions live. Neither runs tickets
        unattended; for that, run `ne2-factory run` instead (see `ne2-factory run
        --help`), which polls every 30s (BACKLOG_POLL_INTERVAL to override) and
        keeps running, picking up refined tickets.

        Tickets don't live on the local filesystem by default — this tool doesn't
        queue tickets itself, a human files them directly on the backend. Which
        backend backs the backlog is set via "Backlog:Provider" in appsettings.json
        ("GitHub", the default, or "File" for plain text files under
        .ne2-factory/backlog):
          GitHub  file new tickets as GitHub issues with the "backlog" label;
                  refined/failed/done are tracked via the "refined" and
                  "backlog:failed" labels and the issue's open/closed state.
          File    create a numbered folder under .ne2-factory/backlog (e.g.
                  .ne2-factory/backlog/42/) containing a ticket.txt with a
                  "State:"/"Title:" header and the ticket body.

        `ne2-factory backlog refine` launches one interactive `claude` session per
        ticket (fresh context each time), using the `refine-ticket` skill, to turn
        queued tickets into refined ones before `ne2-factory run` picks them up —
        that's the only place in the pipeline where clarifying questions get asked;
        the worker never asks anything. Ctrl-C at any point is safe: whatever wasn't
        refined yet stays queued as unrefined for the next `backlog refine` run.
        """;

    public int Run(string[] args)
    {
        var command = args.Length > 0 ? args[0] : "";
        var rest = args.Skip(1).ToArray();
        switch (command)
        {
            case "list": CmdList(); return 0;
            case "refine": return CmdRefine(rest);
            case "requeue": return CmdRequeue(rest);
            case "-h": case "--help": case "": Console.WriteLine(Usage); return 0;
            default:
                logger.LogError("Comando desconocido: {Command}", command);
                Console.WriteLine(Usage);
                return 1;
        }
    }

    private void CmdList()
    {
        Console.WriteLine("En cola, sin refinar:");
        foreach (var item in backlog.ListUnrefined())
            Console.WriteLine($"  #{item.Number}  {item.Title}");

        Console.WriteLine("En cola, lista para implementar:");
        foreach (var item in backlog.ListRefined())
            Console.WriteLine($"  #{item.Number}  {item.Title}");

        Console.WriteLine("Hechos:");
        foreach (var item in backlog.ListDone())
            Console.WriteLine($"  #{item.Number}  {item.Title}");

        Console.WriteLine("Fallidos/bloqueados:");
        foreach (var item in backlog.ListFailed())
            Console.WriteLine($"  #{item.Number}  {item.Title}");
    }

    // Refines one ticket per interactive `claude` session (fresh context each
    // time), re-checking the backlog after each session so state always comes
    // from GitHub/the file backend rather than something we track ourselves.
    // If a session exits without the ticket actually turning refined (Ctrl-C,
    // crash, reviewer bailed), we stop instead of looping on the same ticket.
    private int CmdRefine(string[] args)
    {
        var all = args.Contains("--all");

        while (true)
        {
            var next = backlog.ListUnrefined().OrderBy(i => i.Number).FirstOrDefault();
            if (next is null)
            {
                Console.WriteLine("No hay tickets pendientes de refinamiento.");
                return 0;
            }

            var item = backlog.GetItem(next.Number);
            if (item is null)
            {
                logger.LogWarning("#{Number} ya no existe; lo salto.", next.Number);
                if (!all) return 1;
                continue;
            }

            Console.WriteLine($"Refinando #{item.Number} — {item.Title}");
            agent.RunInteractive(BuildRefinePrompt(item));

            var refined = backlog.GetItem(next.Number)?.State == TicketState.Refined;
            if (!refined)
            {
                logger.LogWarning("#{Number} sigue sin refinar; me detengo aquí.", next.Number);
                return 1;
            }

            Console.WriteLine($"#{next.Number} refinado.");
            if (!all) return 0;
        }
    }

    // Hands the skill everything it needs to know about the ticket up front —
    // mirrors AgentRunJobs.BuildWorkTicketPrompt — so refine-ticket never has
    // to know GitHub exists; only this class and IBacklog do.
    private static string BuildRefinePrompt(BacklogItem item)
    {
        var comments = string.Join("\n", item.Comments);
        var reference = item.Url is null ? $"#{item.Number}" : $"#{item.Number} ({item.Url})";
        return $$"""
            /refine-ticket

            Ticket: {{reference}}

            {{item.Title}}

            {{item.Body ?? ""}}

            {{comments}}
            """;
    }

    private int CmdRequeue(string[] args)
    {
        var n = args.Length > 0 ? args[0] : "";
        if (!int.TryParse(n, out var number))
        {
            logger.LogError("Uso: ne2-factory backlog requeue <número-de-ticket>");
            return 1;
        }
        backlog.Requeue(number);
        Console.WriteLine($"Reencolado: #{number}");
        return 0;
    }
}
