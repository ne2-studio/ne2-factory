using Hangfire;
using Microsoft.Extensions.Logging;
using Ne2Factory.Cli.Backlog;
using Ne2Factory.Cli.Common;
using Ne2Factory.Cli.Services;

namespace Ne2Factory.Cli.FactoryWorker;

// Hangfire job body for a single AgentRun — the equivalent of what used to
// run inline in the worker's foreach before that loop moved to
// background. Executes inside the Hangfire server hosted by `ne2-factory run`.
// No automatic retries: an ambiguous outcome needs a human look, not a
// silent re-run against the same ticket.
[AutomaticRetry(Attempts = 0)]
public sealed class AgentRunJobs(
    IBacklog backlog,
    IAgentRunRepository runs,
    IProcessRunner proc,
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

            logger.LogInformation("Actualizando repo (git pull --ff-only) antes de procesar #{Number}.", number);
            var pullExitCode = proc.RunInherited("git", ["pull", "--ff-only"]);
            if (pullExitCode != 0)
            {
                logger.LogWarning(
                    "-> git pull --ff-only falló (exit {ExitCode}) para #{Number}; lo salto para revisión manual.",
                    pullExitCode, number);
                runs.Finish(runId, AgentRunStatus.Failed, outcome: null,
                    error: $"git pull --ff-only falló (exit {pullExitCode}).");
                return;
            }

            logger.LogInformation("Ticket: #{Number} {Title}", number, current.Title);
            logger.LogInformation("Lanzando @{Agent} en la issue #{Number} ({Url}).", run.AgentName, number,
                current.Url);

            var outcome = agent.GenerateAndProcessResponse(run.AgentName, current);

            if (outcome.Outcome is null)
            {
                logger.LogWarning(
                    "-> la sesión terminó sin un resultado interpretable (salida manual/crash/formato inesperado). Marco #{Number} como fallido para revisión manual.",
                    number);

                backlog.MarkFailed(number);
            }

            runs.Finish(runId, outcome.Status, outcome: outcome.Outcome, error: outcome.Error);
        }
        catch (BacklogException ex)
        {
            logger.LogError("-> no se pudo consultar #{Number} en el backlog: {Error}. Marco el run como fallido para reintentar en el próximo ciclo.", run.IssueNumber, ex.Message);
            runs.Finish(runId, AgentRunStatus.Failed, outcome: null, error: ex.Message);
        }
    }

    // What ExecuteWork/ExecuteRefine settled on, so Execute is the single place
    // that calls runs.Finish (it's the one that owns runId).
    public readonly record struct RunOutcome(AgentRunStatus Status, string? Outcome, string? Error)
    {
        public static RunOutcome Failed(string? outcome, string? error) => new(AgentRunStatus.Failed, outcome, error);
        public static RunOutcome Succeeded(string? outcome) => new(AgentRunStatus.Succeeded, outcome, Error: null);
    }
}