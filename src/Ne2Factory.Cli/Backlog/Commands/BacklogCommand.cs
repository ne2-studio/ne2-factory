using Microsoft.Extensions.Logging;
using Ne2Factory.Cli.Agents;
using Ne2Factory.Cli.FactoryWorker;

namespace Ne2Factory.Cli.Backlog;

internal sealed class BacklogCommand(IBacklog backlog, IAgent agent, ILogger<BacklogCommand> logger)
{
    private const string Usage = """
        Usage: ne2-factory backlog <command>   (from the root of the repo being worked on)

        Commands:
          list               List queued (refined/unrefined/missing-data split)/done/
                                failed tickets.
          refine [--all]     Refine the next unrefined ticket (or, with --all, every
                                unrefined ticket in sequence). See below.
          requeue <number>   Move a failed/blocked/missing-data ticket back into the
                                queue (unrefined).

        All three commands run headless, same as `ne2-factory run`'s worker — there's no
        interactive session anywhere in this pipeline. `list` and `requeue` are purely
        inspection/bookkeeping. `refine` runs one ticket at a time through the same
        `refine-ticket` prompt the worker uses; for continuous unattended processing of
        the whole queue instead, run `ne2-factory run` (see `ne2-factory run --help`),
        which polls every 30s (BACKLOG_POLL_INTERVAL to override).

        Tickets don't live on the local filesystem by default — this tool doesn't
        queue tickets itself, a human files them directly on the backend. Which
        backend backs the backlog is set via "Backlog:Provider" in appsettings.json
        ("GitHub", the default, or "File" for plain text files under
        .ne2-factory/backlog):
          GitHub  file new tickets as GitHub issues with the "backlog" label;
                  refined/missing-data/failed/done are tracked via the "refined",
                  "missing-data", and "backlog:failed" labels and the issue's
                  open/closed state.
          File    create a numbered folder under .ne2-factory/backlog (e.g.
                  .ne2-factory/backlog/42/) containing a ticket.txt with a
                  "State:"/"Title:" header and the ticket body.

        `ne2-factory backlog refine` runs the `refine-ticket` prompt against each
        unrefined ticket in turn, posts its refinement summary (and any open questions)
        as a comment on the ticket, and marks it `refined` or `missing-data` accordingly.
        A ticket marked `missing-data` stays out of the queue — neither `refine` nor
        `ne2-factory run`'s worker will pick it up again — until a human adds the missing
        information and requeues it.
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

        Console.WriteLine("Sin datos suficientes (esperando al humano):");
        foreach (var item in backlog.ListMissingData())
            Console.WriteLine($"  #{item.Number}  {item.Title}");

        Console.WriteLine("Hechos:");
        foreach (var item in backlog.ListDone())
            Console.WriteLine($"  #{item.Number}  {item.Title}");

        Console.WriteLine("Fallidos/bloqueados:");
        foreach (var item in backlog.ListFailed())
            Console.WriteLine($"  #{item.Number}  {item.Title}");
    }

    // Refines one ticket per headless `claude` session (fresh context each
    // time), re-checking the backlog after each session so state always comes
    // from GitHub/the file backend rather than something we track ourselves.
    // If a session ends without a parseable outcome (crash, unexpected exit),
    // we stop instead of looping on the same ticket.
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
            var signal = agent.RunRefinement(PromptTemplates.RefineTicket(item), new AgentOptions { SkipPermissions = true, Agent = "refiner" });
            if (signal is null || string.IsNullOrWhiteSpace(signal.Outcome))
            {
                logger.LogWarning("#{Number}: la sesión terminó sin un resultado interpretable; me detengo aquí.", next.Number);
                return 1;
            }

            var ready = RefinementOutcome.Apply(backlog, next.Number, signal);
            Console.WriteLine(ready
                ? $"#{next.Number} refinado."
                : $"#{next.Number} sigue sin datos suficientes; queda a la espera de más información.");

            if (!all) return 0;
        }
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
