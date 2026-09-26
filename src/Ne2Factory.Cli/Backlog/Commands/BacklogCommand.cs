using Microsoft.Extensions.Logging;

namespace Ne2Factory.Cli.Backlog;

internal sealed class BacklogCommand(IBacklog backlog, ILogger<BacklogCommand> logger)
{
    private const string Usage = """
        Usage: ne2-factory backlog <command>   (from the root of the repo being worked on)

        Commands:
          list               List queued (refined/unrefined split)/done/failed tickets.
          requeue <number>   Move a failed/blocked ticket back into the queue
                                (unrefined).

        This command is purely interactive — it never runs tickets itself. To work
        the queue unattended, run `ne2-factory run` instead (see `ne2-factory run
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

        Run the `refine-backlog` skill interactively (in a normal `claude` session,
        not the worker) to turn queued tickets into refined ones before `ne2-factory
        run` picks them up — that's also the only place in the pipeline where
        clarifying questions get asked; the worker never asks anything.
        """;

    public int Run(string[] args)
    {
        var command = args.Length > 0 ? args[0] : "";
        var rest = args.Skip(1).ToArray();
        switch (command)
        {
            case "list": CmdList(); return 0;
            case "requeue": return CmdRequeue(rest);
            case "-h": case "--help": case "": logger.LogInformation("{Text}", Usage); return 0;
            default:
                logger.LogError("Comando desconocido: {Command}", command);
                logger.LogInformation("{Text}", Usage);
                return 1;
        }
    }

    private void CmdList()
    {
        logger.LogInformation("En cola, sin refinar:");
        foreach (var item in backlog.ListUnrefined())
            logger.LogInformation("  #{Number}  {Title}", item.Number, item.Title);

        logger.LogInformation("En cola, lista para implementar:");
        foreach (var item in backlog.ListRefined())
            logger.LogInformation("  #{Number}  {Title}", item.Number, item.Title);

        logger.LogInformation("Hechos:");
        foreach (var item in backlog.ListDone())
            logger.LogInformation("  #{Number}  {Title}", item.Number, item.Title);

        logger.LogInformation("Fallidos/bloqueados:");
        foreach (var item in backlog.ListFailed())
            logger.LogInformation("  #{Number}  {Title}", item.Number, item.Title);
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
        logger.LogInformation("Reencolado: #{Number}", number);
        return 0;
    }
}
