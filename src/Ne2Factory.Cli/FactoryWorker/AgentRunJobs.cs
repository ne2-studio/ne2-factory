using Hangfire;
using Microsoft.Extensions.Logging;
using Ne2Factory.Cli.Backlog;

namespace Ne2Factory.Cli.FactoryWorker;

// Hangfire job body for a single AgentRun.
//
// Executes inside the Hangfire server hosted by `ne2-factory run`.
//
// No automatic retries: an ambiguous outcome needs a human look, not a
// silent re-run against the same ticket.
[AutomaticRetry(Attempts = 0)]
public sealed class AgentRunJobs(
    IBacklog backlog,
    IAgentRunRepository runs,
    TicketWorkspace workspace,
    Agent agent,
    ILogger<AgentRunJobs> logger)
{
    public void Execute(Guid runId)
    {
        var run = runs.Get(runId);
        if (run is null)
        {
            logger.LogWarning("AgentRun {RunId} no existe; lo salto.", runId);
            return;
        }

        runs.MarkRunning(runId);

        try
        {
            var number = run.IssueNumber;
            var current = backlog.GetItem(number);

            // The ticket's state may have changed between enqueue and now (another
            // process requeued/closed/refined the issue in the meantime); re-verify.
            var expectedState = TicketAgents.ExpectedState(run.AgentName);
            
            if (current is null || expectedState is null || current.State != expectedState)
            {
                logger.LogInformation("Issue #{Number} ya no cumple las condiciones (su estado cambió); lo salto.",
                    number);
                runs.Finish(runId, AgentRunStatus.Cancelled, outcome: null,
                    error: "Issue ya no elegible (su estado cambió).");
                return;
            }

            // Only the implementer changes code, so only it gets its own branch; the
            // refiner just reads the up-to-date base.
            var onTicketBranch = run.AgentName == TicketAgents.Work;

            logger.LogInformation("Actualizando repo antes de procesar #{Number}.", number);
            var prepared = workspace.Prepare(number, onTicketBranch);
            if (prepared.IsFailure)
            {
                logger.LogWarning("-> no se pudo preparar el repo para #{Number}: {Error}. Lo salto para revisión manual.",
                    number, prepared.Error.Message);
                runs.Finish(runId, AgentRunStatus.Failed, outcome: null, error: prepared.Error.Message);
                return;
            }

            var baseBranch = prepared.Value;
            string? outcome = null;
            try
            {
                logger.LogInformation("Ticket: #{Number} {Title}", number, current.Title);
                logger.LogInformation("Lanzando @{Agent} en la issue #{Number} ({Url}).", run.AgentName, number,
                    current.Url);

                var result = agent.GenerateAndProcessResponse(run.AgentName, current, baseBranch);

                if (result.IsFailure)
                {
                    logger.LogWarning("-> {Error}. Marco #{Number} como fallido para revisión manual.",
                        result.Error.Message, number);

                    backlog.MarkFailed(number);
                    runs.Finish(runId, AgentRunStatus.Failed, outcome: null, error: result.Error.Message);
                    return;
                }

                outcome = result.Value;
                runs.Finish(runId, AgentRunStatus.Succeeded, outcome: result.Value, error: null);
            }
            finally
            {
                if (onTicketBranch)
                    workspace.Restore(number, baseBranch, discardBranch: outcome == "blocked");
            }
        }
        catch (BacklogException ex)
        {
            logger.LogError("-> no se pudo consultar #{Number} en el backlog: {Error}. Marco el run como fallido para reintentar en el próximo ciclo.", run.IssueNumber, ex.Message);
            runs.Finish(runId, AgentRunStatus.Failed, outcome: null, error: ex.Message);
        }
    }
}