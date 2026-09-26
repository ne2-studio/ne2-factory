using Microsoft.Extensions.Logging;

namespace Ne2Factory.Cli.Backlog;

internal sealed class BacklogCommand(IBacklog backlog, ILogger<BacklogCommand> logger)
{
    public const string QueueLabel = GithubIssuesBacklog.QueueLabel;
    public const string RefinedLabel = GithubIssuesBacklog.RefinedLabel;
    public const string FailedLabel = GithubIssuesBacklog.FailedLabel;

    private const string Usage = """
        Usage: ne2-factory backlog <command>   (from the root of the repo being worked on)

        Commands:
          list               List queued (refined/unrefined split)/done/failed
                                tickets (GitHub issues).
          requeue <number>   Move a failed/blocked issue back into the queue.

        This command is purely interactive — it never runs tickets itself. To work
        the queue unattended, run `ne2-factory run` instead (see `ne2-factory run
        --help`), which polls every 30s (BACKLOG_POLL_INTERVAL to override) and
        keeps running, picking up tickets labeled both "backlog" and "refined".

        Tickets live as GitHub issues on this repo, not on the local filesystem.
        File new ones directly on GitHub with the "backlog" label — this tool does
        not queue tickets itself:
          queued, not yet refined   open issue,   label "backlog", no "refined"
          queued, ready             open issue,   labels "backlog" + "refined"
          done                      closed issue, label "backlog"
          failed                    open issue,   label "backlog:failed"

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
            logger.LogError("Uso: ne2-factory backlog requeue <número-de-issue>");
            return 1;
        }
        backlog.Requeue(number);
        logger.LogInformation("Reencolado: #{Number}", number);
        return 0;
    }
}
