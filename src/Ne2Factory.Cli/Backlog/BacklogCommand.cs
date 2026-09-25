using Microsoft.Extensions.Logging;
using Ne2Factory.Cli.Agents;
using Ne2Factory.Cli.Services;

namespace Ne2Factory.Cli.Backlog;

internal sealed class BacklogCommand(
    IGitHubCli gitHubCli,
    IProcessRunner proc,
    IAgent agent,
    ProjectContext ctx,
    ILogger<BacklogCommand> logger)
{
    public const string QueueLabel = "backlog";
    public const string RefinedLabel = "refined";
    public const string FailedLabel = "backlog:failed";

    private const string Usage = """
        Usage: ne2-factory backlog <command>   (from the root of the repo being worked on)

        Commands:
          list               List queued (refined/unrefined split)/done/failed
                                tickets (GitHub issues).
          requeue <number>   Move a failed/blocked issue back into the queue.
          run --yolo         Start the worker in the foreground: polls the queue
                                every 30s (BACKLOG_POLL_INTERVAL to override) and
                                keeps running, picking up tickets labeled both
                                "backlog" and "refined" — no need to restart it per
                                ticket. Also hosts the background job server that
                                processes `gap-scout scan` jobs queued while it
                                runs. Stop with Ctrl-C.
                              --yolo is mandatory: every ticket runs headless via
                                `claude --print --dangerously-skip-permissions`, so
                                there is never anyone to answer a prompt — only
                                run this when you're comfortable leaving it fully
                                unattended.

        The worker stops (exits) if a ticket ends without a clear done/blocked
        signal — that needs a human look before resuming; requeue/fix the issue
        and run `backlog run --yolo` again.

        Tickets live as GitHub issues on this repo, not on the local filesystem.
        File new ones directly on GitHub with the "backlog" label — this tool does
        not queue tickets itself:
          queued, not yet refined   open issue,   label "backlog", no "refined"
          queued, ready             open issue,   labels "backlog" + "refined"
          done                      closed issue, label "backlog"
          failed                    open issue,   label "backlog:failed"

        Run the `refine-backlog` skill interactively (in a normal `claude` session,
        not this worker) to turn queued tickets into refined ones before `run`
        picks them up — that's also the only place in the pipeline where
        clarifying questions get asked; this worker never asks anything.

        Each refined ticket runs in its own fresh, non-interactive `claude --print`
        session (the work-ticket skill) with permission prompts skipped entirely,
        so git commit/push happen straight to the project's default branch with no
        approval step. Requires `gh` authenticated against this repo.
        """;

    public int Run(string[] args)
    {
        var command = args.Length > 0 ? args[0] : "";
        var rest = args.Skip(1).ToArray();
        switch (command)
        {
            case "list": CmdList(); return 0;
            case "requeue": return CmdRequeue(rest);
            case "run":
                // Valid usage (`run --yolo`) is intercepted in Program.cs, which starts
                // the host instead of calling this method — only invalid usage lands here.
                logger.LogError("Uso: ne2-factory backlog run --yolo");
                logger.LogInformation("{Text}", Usage);
                return 1;
            case "-h": case "--help": case "": logger.LogInformation("{Text}", Usage); return 0;
            default:
                logger.LogError("Comando desconocido: {Command}", command);
                logger.LogInformation("{Text}", Usage);
                return 1;
        }
    }

    public void EnsureLabels()
    {
        gitHubCli.CreateLabelSilently(QueueLabel, "0E8A16", "Backlog ticket queued for bin/backlog");
        gitHubCli.CreateLabelSilently(RefinedLabel, "0E8A16", "Ticket refinado, listo para bin/backlog");
        gitHubCli.CreateLabelSilently(FailedLabel, "B60205", "Backlog ticket blocked, needs review before requeuing");
    }

    private void CmdList()
    {
        logger.LogInformation("En cola, sin refinar:");
        foreach (var issue in gitHubCli.ListIssues([QueueLabel], "open", "number,title,labels"))
        {
            if (issue.Labels?.Any(l => l.Name == RefinedLabel) != true)
                logger.LogInformation("  #{Number}  {Title}", issue.Number, issue.Title);
        }

        logger.LogInformation("En cola, lista para implementar:");
        foreach (var issue in gitHubCli.ListIssues([QueueLabel, RefinedLabel], "open", "number,title"))
            logger.LogInformation("  #{Number}  {Title}", issue.Number, issue.Title);

        logger.LogInformation("Hechos:");
        foreach (var issue in gitHubCli.ListIssues([QueueLabel], "closed", "number,title"))
            logger.LogInformation("  #{Number}  {Title}", issue.Number, issue.Title);

        logger.LogInformation("Fallidos/bloqueados:");
        foreach (var issue in gitHubCli.ListIssues([FailedLabel], "open", "number,title"))
            logger.LogInformation("  #{Number}  {Title}", issue.Number, issue.Title);
    }

    private int CmdRequeue(string[] args)
    {
        var n = args.Length > 0 ? args[0] : "";
        if (!int.TryParse(n, out var number))
        {
            logger.LogError("Uso: ne2-factory backlog requeue <número-de-issue>");
            return 1;
        }
        gitHubCli.EditIssueLabels(number, FailedLabel, QueueLabel);
        logger.LogInformation("Reencolado: #{Number}", number);
        return 0;
    }

    // Processes every currently queued issue once. Returns false if it hit a
    // condition that needs human review before polling should resume.
    public bool ProcessQueue()
    {
        var issues = gitHubCli.ListIssues([QueueLabel, RefinedLabel], "open", "number")
            .Select(i => i.Number)
            .OrderBy(n => n)
            .ToArray();

        if (issues.Length == 0)
        {
            logger.LogInformation("No hay issues por procesar.");
            return true;
        }

        logger.LogInformation("Vistos {Count} tickets en cola: {Numbers}", issues.Length, string.Join(", ", issues.Select(n => $"#{n}")));

        var seen = 0;
        foreach (var n in issues)
        {
            // Otro proceso pudo haber reencolado/movido el issue entre tanto; re-verifica.
            var current = gitHubCli.ViewIssue(n, "state,labels");
            if (current is null) continue;
            var labelNames = current.Labels?.Select(l => l.Name).ToHashSet() ?? [];
            if (current.State != "OPEN" || !labelNames.Contains(QueueLabel) || !labelNames.Contains(RefinedLabel))
            {
                logger.LogInformation("Issue #{Number} ya no cumple las condiciones (estado/labels cambiaron); lo salto.", n);
                continue;
            }

            logger.LogInformation("Actualizando repo (git pull --ff-only) antes de procesar #{Number}.", n);
            proc.RunInherited("git", ["pull", "--ff-only"]);

            if (File.Exists(ctx.SignalFile)) File.Delete(ctx.SignalFile);

            var issueJson = gitHubCli.ViewIssue(n, "title,body,url,comments");
            if (issueJson is null) continue;
            var title = issueJson.Title;
            var body = issueJson.Body ?? "";
            var url = issueJson.Url;
            var comments = string.Join("\n", (issueJson.Comments ?? [])
                .Select(c => $"-- comment by {c.Author.Login} ({c.CreatedAt}) --\n{c.Body}"));

            seen++;
            logger.LogInformation("Ticket: #{Number} {Title}", n, title);
            logger.LogInformation("Lanzando /work-ticket en la issue #{Number} ({Url}).", n, url);

            var prompt = $"/work-ticket\n\nGitHub issue: #{n} ({url})\n\n{title}\n\n{body}\n\n{comments}";
            agent.Run(prompt, new AgentOptions { SkipPermissions = true });

            if (File.Exists(ctx.SignalFile))
            {
                var lines = File.ReadAllLines(ctx.SignalFile);
                var status = lines.FirstOrDefault(l => l.StartsWith("status="))?["status=".Length..];
                var reason = lines.FirstOrDefault(l => l.StartsWith("reason="))?["reason=".Length..];

                switch (status)
                {
                    case "done":
                        gitHubCli.CloseIssue(n);
                        logger.LogInformation("-> hecho: #{Number}", n);
                        break;
                    case "blocked":
                        gitHubCli.EditIssueLabels(n, QueueLabel, FailedLabel);
                        logger.LogInformation("-> bloqueado: #{Number}{Reason}. Revisa y usa 'requeue {Number}' si procede.", n, string.IsNullOrEmpty(reason) ? "" : $" ({reason})", n);
                        break;
                    default:
                        logger.LogWarning("-> señal desconocida ('{Status}'), dejo #{Number} en cola para revisión manual.", status, n);
                        return false;
                }
                File.Delete(ctx.SignalFile);
            }
            else
            {
                logger.LogWarning("-> la sesión terminó sin señal (salida manual/crash). Dejo #{Number} en cola y paro.", n);
                return false;
            }
        }

        logger.LogInformation("Pasada de cola completada. Vistos: {Seen}/{Total} tickets.", seen, issues.Length);
        return true;
    }
}
