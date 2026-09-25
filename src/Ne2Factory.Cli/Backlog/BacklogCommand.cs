using System.Text.Json;
using Hangfire;

namespace Ne2Factory.Cli.Backlog;

internal static class BacklogCommand
{
    private const string QueueLabel = "backlog";
    private const string RefinedLabel = "refined";
    private const string FailedLabel = "backlog:failed";

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

    public static int Run(string[] args, ProjectContext ctx)
    {
        var command = args.Length > 0 ? args[0] : "";
        var rest = args.Skip(1).ToArray();
        switch (command)
        {
            case "list": CmdList(); return 0;
            case "requeue": return CmdRequeue(rest);
            case "run": return CmdRun(rest, ctx);
            case "-h": case "--help": case "": Console.WriteLine(Usage); return 0;
            default:
                Console.Error.WriteLine($"Comando desconocido: {command}");
                Console.WriteLine(Usage);
                return 1;
        }
    }

    private static void EnsureLabels()
    {
        GitHubCli.CreateLabelSilently(QueueLabel, "0E8A16", "Backlog ticket queued for bin/backlog");
        GitHubCli.CreateLabelSilently(RefinedLabel, "0E8A16", "Ticket refinado, listo para bin/backlog");
        GitHubCli.CreateLabelSilently(FailedLabel, "B60205", "Backlog ticket blocked, needs review before requeuing");
    }

    private static void CmdList()
    {
        Console.WriteLine("En cola, sin refinar:");
        foreach (var issue in GitHubCli.ListIssues([QueueLabel], "open", "number,title,labels"))
        {
            if (issue.Labels?.Any(l => l.Name == RefinedLabel) != true)
                Console.WriteLine($"  #{issue.Number}  {issue.Title}");
        }

        Console.WriteLine("En cola, lista para implementar:");
        foreach (var issue in GitHubCli.ListIssues([QueueLabel, RefinedLabel], "open", "number,title"))
            Console.WriteLine($"  #{issue.Number}  {issue.Title}");

        Console.WriteLine("Hechos:");
        foreach (var issue in GitHubCli.ListIssues([QueueLabel], "closed", "number,title"))
            Console.WriteLine($"  #{issue.Number}  {issue.Title}");

        Console.WriteLine("Fallidos/bloqueados:");
        foreach (var issue in GitHubCli.ListIssues([FailedLabel], "open", "number,title"))
            Console.WriteLine($"  #{issue.Number}  {issue.Title}");
    }

    private static int CmdRequeue(string[] args)
    {
        var n = args.Length > 0 ? args[0] : "";
        if (!int.TryParse(n, out var number))
        {
            Console.Error.WriteLine("Uso: ne2-factory backlog requeue <número-de-issue>");
            return 1;
        }
        GitHubCli.EditIssueLabels(number, FailedLabel, QueueLabel);
        Console.WriteLine($"Reencolado: #{number}");
        return 0;
    }

    private static int CmdRun(string[] args, ProjectContext ctx)
    {
        if (args.Length == 0 || args[0] != "--yolo")
        {
            Console.Error.WriteLine("Uso: ne2-factory backlog run --yolo");
            Console.WriteLine(Usage);
            return 1;
        }

        Directory.CreateDirectory(ctx.BacklogDir);
        EnsureLabels();

        var dbPath = HangfireSetup.ConfigureStorage(ctx);
        using var server = new BackgroundJobServer();
        Console.WriteLine($"Servidor de jobs en background arrancado (gap-scout scan usa {dbPath}).");

        Console.WriteLine($"Escuchando issues con label '{QueueLabel}' (cada {ctx.BacklogPollIntervalSeconds}s). Ctrl-C para parar.");
        while (true)
        {
            if (!ProcessQueue(ctx))
            {
                Console.WriteLine("Worker detenido (revisión manual necesaria antes de reanudar).");
                return 1;
            }
            Thread.Sleep(TimeSpan.FromSeconds(ctx.BacklogPollIntervalSeconds));
        }
    }

    // Processes every currently queued issue once. Returns false if it hit a
    // condition that needs human review before polling should resume.
    private static bool ProcessQueue(ProjectContext ctx)
    {
        var claudeFlags = new[] { "--print", "--dangerously-skip-permissions" };
        var issues = GitHubCli.ListIssues([QueueLabel, RefinedLabel], "open", "number")
            .Select(i => i.Number)
            .OrderBy(n => n)
            .ToArray();

        if (issues.Length == 0) return true;

        var seen = 0;
        foreach (var n in issues)
        {
            // Otro proceso pudo haber reencolado/movido el issue entre tanto; re-verifica.
            var current = GitHubCli.ViewIssue(n, "state,labels");
            if (current is null) continue;
            var labelNames = current.Labels?.Select(l => l.Name).ToHashSet() ?? [];
            if (current.State != "OPEN" || !labelNames.Contains(QueueLabel) || !labelNames.Contains(RefinedLabel))
                continue;

            Proc.RunInherited("git", ["pull", "--ff-only"]);

            if (File.Exists(ctx.SignalFile)) File.Delete(ctx.SignalFile);

            var issueJson = GitHubCli.ViewIssue(n, "title,body,url,comments");
            if (issueJson is null) continue;
            var title = issueJson.Title;
            var body = issueJson.Body ?? "";
            var url = issueJson.Url;
            var comments = string.Join("\n", (issueJson.Comments ?? [])
                .Select(c => $"-- comment by {c.Author.Login} ({c.CreatedAt}) --\n{c.Body}"));

            seen++;
            Console.WriteLine();
            Console.WriteLine("==================================================");
            Console.WriteLine($"Ticket: #{n} {title}");
            Console.WriteLine("==================================================");

            var prompt = $"/work-ticket\n\nGitHub issue: #{n} ({url})\n\n{title}\n\n{body}\n\n{comments}";
            Proc.RunInherited("claude", [.. claudeFlags, prompt]);

            if (File.Exists(ctx.SignalFile))
            {
                var lines = File.ReadAllLines(ctx.SignalFile);
                var status = lines.FirstOrDefault(l => l.StartsWith("status="))?["status=".Length..];
                var reason = lines.FirstOrDefault(l => l.StartsWith("reason="))?["reason=".Length..];

                switch (status)
                {
                    case "done":
                        GitHubCli.CloseIssue(n);
                        Console.WriteLine($"-> hecho: #{n}");
                        break;
                    case "blocked":
                        GitHubCli.EditIssueLabels(n, QueueLabel, FailedLabel);
                        Console.WriteLine($"-> bloqueado: #{n}{(string.IsNullOrEmpty(reason) ? "" : $" ({reason})")}. Revisa y usa 'requeue {n}' si procede.");
                        break;
                    default:
                        Console.WriteLine($"-> señal desconocida ('{status}'), dejo #{n} en cola para revisión manual.");
                        return false;
                }
                File.Delete(ctx.SignalFile);
            }
            else
            {
                Console.WriteLine($"-> la sesión terminó sin señal (salida manual/crash). Dejo #{n} en cola y paro.");
                return false;
            }
        }

        Console.WriteLine();
        Console.WriteLine($"Pasada de cola completada. Vistos: {seen}/{issues.Length} tickets.");
        return true;
    }
}
