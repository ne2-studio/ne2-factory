using Microsoft.Extensions.Logging;

namespace Ne2Factory.Cli.Backlog;

internal sealed class BacklogCommand(IBacklog backlog, ILogger<BacklogCommand> logger)
{
    private const string Usage = """
        Usage: ne2-factory backlog <command>   (from the root of the repo being worked on)

        Commands:
          list               List queued (refined/unrefined/missing-data split)/done/
                                failed tickets.
          requeue <number>   Move a failed/blocked/missing-data ticket back into the
                                queue (unrefined).

        Both commands run headless, same as `ne2-factory run`'s worker — there's no
        interactive session anywhere in this pipeline. They are purely
        inspection/bookkeeping. For refinement and continuous unattended processing of
        the whole queue, run `ne2-factory run` (see `ne2-factory run --help`),
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
        """;

    public int Run(string[] args)
    {
        var command = args.Length > 0 ? args[0] : "";
        var rest = args.Skip(1).ToArray();
        switch (command)
        {
            case "list": CmdList(); return 0;
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
        PrintOrError(backlog.ListUnrefined);

        Console.WriteLine("En cola, lista para implementar:");
        PrintOrError(backlog.ListRefined);

        Console.WriteLine("Sin datos suficientes (esperando al humano):");
        PrintOrError(backlog.ListMissingData);

        Console.WriteLine("Hechos:");
        PrintOrError(backlog.ListDone);

        Console.WriteLine("Fallidos/bloqueados:");
        PrintOrError(backlog.ListFailed);
    }

    private void PrintOrError(Func<IReadOnlyList<BacklogItem>> list)
    {
        try
        {
            foreach (var item in list())
                Console.WriteLine($"  #{item.Number}  {item.Title}");
        }
        catch (BacklogException ex)
        {
            logger.LogError("  (no se pudo listar: {Error})", ex.Message);
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
        try
        {
            backlog.Requeue(number);
        }
        catch (BacklogException ex)
        {
            logger.LogError("No se pudo reencolar #{Number}: {Error}", number, ex.Message);
            return 1;
        }
        Console.WriteLine($"Reencolado: #{number}");
        return 0;
    }
}
