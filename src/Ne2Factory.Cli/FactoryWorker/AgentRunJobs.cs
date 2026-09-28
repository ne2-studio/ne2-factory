using Hangfire;
using Microsoft.Extensions.Logging;
using Ne2Factory.Cli.Agents;
using Ne2Factory.Cli.Backlog;
using Ne2Factory.Cli.Services;

namespace Ne2Factory.Cli.FactoryWorker;

// Hangfire job body for a single AgentRun — the equivalent of what used to
// run inline in BacklogQueueProcessor's foreach before that loop moved to
// background. Executes inside the Hangfire server hosted by `ne2-factory run`.
// No automatic retries: an ambiguous outcome needs a human look, not a
// silent re-run against the same ticket.
[AutomaticRetry(Attempts = 0)]
internal sealed class AgentRunJobs(
    IBacklog backlog,
    IAgentRunRepository runs,
    IProcessRunner proc,
    IAgent agent,
    ILogger<AgentRunJobs> logger)
{
    private const string WorkTicketAgent = "work-ticket";
    private const string RefineTicketAgent = "refine-ticket";

    public void Execute(Guid runId)
    {
        var run = runs.Get(runId);
        if (run is null)
        {
            logger.LogWarning("AgentRun {RunId} no existe; lo salto.", runId);
            return;
        }

        runs.MarkRunning(runId);
        var number = run.IssueNumber;

        // The ticket's state may have changed between enqueue and now (another
        // process requeued/closed/refined the issue in the meantime); re-verify.
        var expectedState = run.AgentName switch
        {
            WorkTicketAgent => TicketState.Refined,
            RefineTicketAgent => TicketState.Unrefined,
            _ => (TicketState?)null,
        };

        var current = backlog.GetItem(number);
        if (current is null || expectedState is null || current.State != expectedState)
        {
            logger.LogInformation("Issue #{Number} ya no cumple las condiciones (su estado cambió); lo salto.", number);
            runs.Finish(runId, AgentRunStatus.Cancelled, outcome: null, error: "Issue ya no elegible (su estado cambió).");
            return;
        }

        logger.LogInformation("Actualizando repo (git pull --ff-only) antes de procesar #{Number}.", number);
        proc.RunInherited("git", ["pull", "--ff-only"]);

        logger.LogInformation("Ticket: #{Number} {Title}", number, current.Title);
        logger.LogInformation("Lanzando /{Agent} en la issue #{Number} ({Url}).", run.AgentName, number, current.Url);

        if (run.AgentName == RefineTicketAgent)
        {
            ExecuteRefine(runId, number, current);
            return;
        }

        var prompt = PromptTemplates.WorkTicket(current);
        var outcome = agent.Run(prompt, new AgentOptions { SkipPermissions = true });
        if (outcome is null)
        {
            logger.LogWarning("-> la sesión terminó sin un resultado interpretable (salida manual/crash/formato inesperado). Marco #{Number} como fallido para revisión manual.", number);
            backlog.MarkFailed(number);
            runs.Finish(runId, AgentRunStatus.Failed, outcome: null, error: "Sesión sin resultado interpretable.");
            return;
        }

        switch (outcome.Status)
        {
            case "done":
                backlog.Close(number);
                runs.Finish(runId, AgentRunStatus.Succeeded, outcome.Status, error: null);
                logger.LogInformation("-> hecho: #{Number}", number);
                break;
            case "blocked":
                backlog.MarkFailed(number);
                runs.Finish(runId, AgentRunStatus.Failed, outcome.Status, outcome.Reason);
                logger.LogInformation("-> bloqueado: #{Number}{Reason}. Revisa y usa 'requeue {Number}' si procede.", number, string.IsNullOrEmpty(outcome.Reason) ? "" : $" ({outcome.Reason})", number);
                break;
            default:
                logger.LogWarning("-> resultado desconocido ('{Status}'), marco #{Number} como fallido para revisión manual.", outcome.Status, number);
                backlog.MarkFailed(number);
                runs.Finish(runId, AgentRunStatus.Failed, outcome.Status, error: $"Resultado desconocido: {outcome.Status}");
                break;
        }
    }

    private void ExecuteRefine(Guid runId, int number, BacklogItem current)
    {
        var prompt = PromptTemplates.RefineTicket(current);
        var signal = agent.RunRefinement(prompt, new AgentOptions { SkipPermissions = true });
        if (signal is null || string.IsNullOrWhiteSpace(signal.Outcome))
        {
            logger.LogWarning("-> la sesión terminó sin un resultado interpretable (salida manual/crash/formato inesperado). Marco #{Number} como fallido para revisión manual.", number);
            backlog.MarkFailed(number);
            runs.Finish(runId, AgentRunStatus.Failed, outcome: null, error: "Sesión sin resultado interpretable.");
            return;
        }

        var ready = RefinementOutcome.Apply(backlog, number, signal);
        runs.Finish(runId, AgentRunStatus.Succeeded, signal.Outcome, error: null);
        logger.LogInformation(ready ? "-> refinado: #{Number}" : "-> #{Number} sigue sin datos suficientes; queda a la espera de más información.", number);
    }
}
