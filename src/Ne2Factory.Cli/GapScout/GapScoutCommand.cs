using Hangfire;

namespace Ne2Factory.Cli.GapScout;

internal static class GapScoutCommand
{
    private const string Label = "gap-scout";

    private const string Usage = """
        Usage: ne2-factory gap-scout <command>   (from the root of the repo being worked on)

        Commands:
          scan <scope|all> [--yolo]
                                Queue a background job per scope that spawns the
                                  architecture-gap-scout agent, one `claude` session
                                  per scope. Scopes, and what "all" expands to,
                                  come from GAP_SCOUT_SCOPES in .ne2-factory.env
                                  (default: app api admin). Returns as soon as the
                                  job(s) are queued; doesn't wait for them to
                                  finish. Jobs are processed by the job server
                                  hosted inside `backlog run --yolo` — that must be
                                  running for queued scans to actually execute.
                                  Jobs persist in a SQLite-backed queue
                                  (.backlog/gap-scout.db), so a scan queued before
                                  the worker is up still runs once it starts. Each
                                  job dedupes against already-filed initiatives and
                                  files new ones as GitHub issues (label
                                  "gap-scout"). Read-only over the code; only
                                  creates GitHub issues (and, once, the "gap-scout"
                                  label).
                                  If a scan for that scope is already queued or
                                  running, it's skipped instead of queueing a
                                  duplicate.

        Without --yolo, `claude` runs with a narrow --allowed-tools list (only gh
        issue/label). A command outside that list pauses for interactive approval
        — but a queued job has no attached terminal to answer it on, so it just
        hangs until the job's own timeout. Use --yolo unless you plan to watch the
        job's output live.

        --yolo runs claude with --dangerously-skip-permissions instead: no prompts
        at all. Reasonable specifically for this agent (unlike work-ticket's
        --yolo, which also skips the commit/push approval): architecture-gap-scout
        never touches git or edits code, so the only thing being unblocked is
        shell access to a read-only inspection, not a write path. Still no
        technical safety net if something in the repo's content tried to steer the
        agent.

        Filed issues are not queued for execution by themselves. Review one, and
        if you want it done, add the "backlog" label — that hands it to
        `backlog`'s existing worker, same as any other ticket. Leaving it
        unlabeled (or closing it) means "not now"; it won't be re-filed verbatim
        on the next scan.

        Requires `gh`. See the factory's docs/backlog/gap-scout.md for how to
        schedule this with a systemd timer.
        """;

    public static int Run(string[] args, ProjectContext ctx)
    {
        var command = args.Length > 0 ? args[0] : "";
        var rest = args.Skip(1).ToArray();
        return command switch
        {
            "scan" => CmdScan(rest, ctx),
            "-h" or "--help" or "" => Print(Usage),
            _ => Unknown(command)
        };
    }

    private static int Print(string text) { Console.WriteLine(text); return 0; }

    private static int Unknown(string command)
    {
        Console.Error.WriteLine($"Comando desconocido: {command}");
        Console.WriteLine(Usage);
        return 1;
    }

    private static int CmdScan(string[] args, ProjectContext ctx)
    {
        var target = args.Length > 0 ? args[0] : "";
        var usage = $"Uso: ne2-factory gap-scout scan <scope|all> [--yolo]  (scopes: {string.Join(' ', ctx.GapScoutScopes)})";

        var yolo = false;
        switch (args.Length > 1 ? args[1] : "")
        {
            case "--yolo": yolo = true; break;
            case "": break;
            default:
                Console.Error.WriteLine(usage);
                return 1;
        }

        string[] scopes;
        if (target == "all")
        {
            scopes = ctx.GapScoutScopes;
        }
        else if (target.Length > 0 && ctx.GapScoutScopes.Contains(target))
        {
            scopes = [target];
        }
        else
        {
            Console.Error.WriteLine(usage);
            return 1;
        }

        HangfireSetup.ConfigureStorage(ctx);

        if (!GitHubCli.TryCreateLabel(Label, "5319E7", "Architecture initiative proposed by gap-scout, pending approval", out var error) && error is not null)
            Console.Error.WriteLine($"aviso: no se pudo crear la label '{Label}': {error}");

        var monitoring = JobStorage.Current.GetMonitoringApi();
        foreach (var scope in scopes)
        {
            var normalizedScope = scope.TrimEnd('/') + "/";
            if (IsAlreadyQueued(monitoring, normalizedScope))
            {
                Console.Error.WriteLine($"aviso: ya hay un scan encolado o en curso para el scope '{normalizedScope}'; lo salto.");
                continue;
            }

            var jobId = BackgroundJob.Enqueue<GapScoutJobs>(job => job.RunScan(normalizedScope, yolo));
            Console.WriteLine($"Encolado: {jobId} (scope: {normalizedScope})");
        }

        Console.WriteLine("El job server vive dentro de 'backlog run --yolo'; sin él corriendo, estos jobs quedan pendientes hasta que se levante.");
        return 0;
    }

    private static bool IsAlreadyQueued(Hangfire.Storage.IMonitoringApi monitoring, string scope)
    {
        bool MatchesScope(Hangfire.Common.Job job) =>
            job.Type == typeof(GapScoutJobs) && job.Args.Count > 0 && (job.Args[0] as string) == scope;

        var enqueued = monitoring.EnqueuedJobs("default", 0, 1000).Any(j => MatchesScope(j.Value.Job));
        if (enqueued) return true;

        var processing = monitoring.ProcessingJobs(0, 1000).Any(j => MatchesScope(j.Value.Job));
        if (processing) return true;

        var scheduled = monitoring.ScheduledJobs(0, 1000).Any(j => MatchesScope(j.Value.Job));
        return scheduled;
    }
}
