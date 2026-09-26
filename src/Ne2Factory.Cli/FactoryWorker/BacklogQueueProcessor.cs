using Microsoft.Extensions.Logging;
using Ne2Factory.Cli.Agents;
using Ne2Factory.Cli.Backlog;
using Ne2Factory.Cli.Services;

namespace Ne2Factory.Cli.FactoryWorker;

// The queue-draining logic behind `ne2-factory run`, split out of BacklogCommand
// (which stays purely interactive) so the worker doesn't depend on a command class.
internal sealed class BacklogQueueProcessor(
    IBacklog backlog,
    IProcessRunner proc,
    IAgent agent,
    IAgentSignalChannel signal,
    ILogger<BacklogQueueProcessor> logger)
{
    private const string QueueLabel = GithubIssuesBacklog.QueueLabel;
    private const string RefinedLabel = GithubIssuesBacklog.RefinedLabel;

    // Processes every currently queued issue once. Returns false if it hit a
    // condition that needs human review before polling should resume.
    public bool ProcessQueue()
    {
        var issues = backlog.ListRefined()
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
            var current = backlog.GetItem(n);
            if (current is null) continue;
            if (current.State != "OPEN" || !current.Labels.Contains(QueueLabel) || !current.Labels.Contains(RefinedLabel))
            {
                logger.LogInformation("Issue #{Number} ya no cumple las condiciones (estado/labels cambiaron); lo salto.", n);
                continue;
            }

            logger.LogInformation("Actualizando repo (git pull --ff-only) antes de procesar #{Number}.", n);
            proc.RunInherited("git", ["pull", "--ff-only"]);

            signal.Reset();

            seen++;
            logger.LogInformation("Ticket: #{Number} {Title}", n, current.Title);
            logger.LogInformation("Lanzando /work-ticket en la issue #{Number} ({Url}).", n, current.Url);

            agent.Run(BuildWorkTicketPrompt(current), new AgentOptions { SkipPermissions = true });

            var outcome = signal.Read();
            if (outcome is null)
            {
                logger.LogWarning("-> la sesión terminó sin señal (salida manual/crash). Dejo #{Number} en cola y paro.", n);
                return false;
            }

            switch (outcome.Status)
            {
                case "done":
                    backlog.Close(n);
                    logger.LogInformation("-> hecho: #{Number}", n);
                    break;
                case "blocked":
                    backlog.MarkFailed(n);
                    logger.LogInformation("-> bloqueado: #{Number}{Reason}. Revisa y usa 'requeue {Number}' si procede.", n, string.IsNullOrEmpty(outcome.Reason) ? "" : $" ({outcome.Reason})", n);
                    break;
                default:
                    logger.LogWarning("-> señal desconocida ('{Status}'), dejo #{Number} en cola para revisión manual.", outcome.Status, n);
                    return false;
            }
        }

        logger.LogInformation("Pasada de cola completada. Vistos: {Seen}/{Total} tickets.", seen, issues.Length);
        return true;
    }

    private static string BuildWorkTicketPrompt(BacklogItem item)
    {
        var comments = string.Join("\n", item.Comments);
        return $"/work-ticket\n\nGitHub issue: #{item.Number} ({item.Url})\n\n{item.Title}\n\n{item.Body ?? ""}\n\n{comments}";
    }
}
