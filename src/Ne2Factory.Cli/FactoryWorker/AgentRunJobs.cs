using Hangfire;
using Microsoft.Extensions.Logging;
using Ne2Factory.Cli.Agents;
using Ne2Factory.Cli.Backlog;
using Ne2Factory.Cli.Common;
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
        var expectedState = TicketAgents.ExpectedState(run.AgentName);

        var currentResult = backlog.GetItem(number);
        if (currentResult.IsFailure)
        {
            logger.LogError("-> no se pudo consultar #{Number} en el backlog: {Error}. Marco el run como fallido para reintentar en el próximo ciclo.", number, currentResult.Error.Message);
            runs.Finish(runId, AgentRunStatus.Failed, outcome: null, error: currentResult.Error.Message);
            return;
        }

        var current = currentResult.Value;
        if (current is null || expectedState is null || current.State != expectedState)
        {
            logger.LogInformation("Issue #{Number} ya no cumple las condiciones (su estado cambió); lo salto.", number);
            runs.Finish(runId, AgentRunStatus.Cancelled, outcome: null, error: "Issue ya no elegible (su estado cambió).");
            return;
        }

        logger.LogInformation("Actualizando repo (git pull --ff-only) antes de procesar #{Number}.", number);
        var pullExitCode = proc.RunInherited("git", ["pull", "--ff-only"]);
        if (pullExitCode != 0)
        {
            logger.LogWarning("-> git pull --ff-only falló (exit {ExitCode}) para #{Number}; lo salto para revisión manual.", pullExitCode, number);
            runs.Finish(runId, AgentRunStatus.Failed, outcome: null, error: $"git pull --ff-only falló (exit {pullExitCode}).");
            return;
        }

        logger.LogInformation("Ticket: #{Number} {Title}", number, current.Title);
        logger.LogInformation("Lanzando @{Agent} en la issue #{Number} ({Url}).", run.AgentName, number, current.Url);

        var outcome = run.AgentName == TicketAgents.Refine
            ? ExecuteRefine(current)
            : ExecuteWork(current);

        runs.Finish(runId, outcome.Status, outcome.Outcome, outcome.Error);
    }

    // What ExecuteWork/ExecuteRefine settled on, so Execute is the single place
    // that calls runs.Finish (it's the one that owns runId).
    private readonly record struct RunOutcome(AgentRunStatus Status, string? Outcome, string? Error)
    {
        public static RunOutcome Failed(string? outcome, string? error) => new(AgentRunStatus.Failed, outcome, error);
        public static RunOutcome Succeeded(string? outcome) => new(AgentRunStatus.Succeeded, outcome, Error: null);
    }

    private RunOutcome ExecuteWork(BacklogItem current)
    {
        var number = current.Number;
        var prompt = PromptTemplates.WorkTicket(current);
        var response = agent.RunWithStructuredOutput<ImplementerResponse>(prompt, new AgentOptions { SkipPermissions = true, Agent = TicketAgents.Work }).Result;
        if (response is null)
        {
            logger.LogWarning("-> la sesión terminó sin un resultado interpretable (salida manual/crash/formato inesperado). Marco #{Number} como fallido para revisión manual.", number);
            LogIfFailed(backlog.MarkFailed(number), number);
            return RunOutcome.Failed(outcome: null, error: "Sesión sin resultado interpretable.");
        }

        switch (response.Status)
        {
            case "done":
            {
                var comment = backlog.Comment(number, FormatWorkTicketComment(response));
                var close = comment.IsSuccess ? backlog.Close(number) : comment;
                if (close.IsFailure)
                {
                    logger.LogError("-> fallo actualizando el backlog para #{Number}: {Error}", number, close.Error.Message);
                    return RunOutcome.Failed(response.Status, error: close.Error.Message);
                }
                logger.LogInformation("-> hecho: #{Number}", number);
                return RunOutcome.Succeeded(response.Status);
            }
            case "blocked":
            {
                var comment = backlog.Comment(number, FormatWorkTicketComment(response));
                var markFailed = comment.IsSuccess ? backlog.MarkFailed(number) : comment;
                if (markFailed.IsFailure)
                    logger.LogError("-> fallo actualizando el backlog para #{Number}: {Error}", number, markFailed.Error.Message);
                logger.LogInformation("-> bloqueado: #{Number}{Reason}. Revisa y usa 'requeue {Number}' si procede.", number, string.IsNullOrEmpty(response.Reason) ? "" : $" ({response.Reason})", number);
                return RunOutcome.Failed(response.Status, response.Reason);
            }
            default:
                logger.LogWarning("-> resultado desconocido ('{Status}'), marco #{Number} como fallido para revisión manual.", response.Status, number);
                LogIfFailed(backlog.MarkFailed(number), number);
                return RunOutcome.Failed(response.Status, error: $"Resultado desconocido: {response.Status}");
        }
    }

    private void LogIfFailed(Result result, int number)
    {
        if (result.IsFailure)
            logger.LogError("-> fallo actualizando el backlog para #{Number}: {Error}", number, result.Error.Message);
    }

    private static string FormatWorkTicketComment(ImplementerResponse response)
    {
        var body = !string.IsNullOrWhiteSpace(response.Summary) ? response.Summary : response.Reason;
        return $"## {(response.Status == "done" ? "Done" : "Blocked")}\n\n{body ?? "(sin resumen)"}";
    }

    private RunOutcome ExecuteRefine(BacklogItem current)
    {
        var number = current.Number;
        var prompt = PromptTemplates.RefineTicket(current);
        var response = agent.RunWithStructuredOutput<RefinerResponse>(prompt, new AgentOptions { SkipPermissions = true, Agent = TicketAgents.Refine }).Result;
        if (response is null || string.IsNullOrWhiteSpace(response.Outcome))
        {
            logger.LogWarning("-> la sesión terminó sin un resultado interpretable (salida manual/crash/formato inesperado). Marco #{Number} como fallido para revisión manual.", number);
            LogIfFailed(backlog.MarkFailed(number), number);
            return RunOutcome.Failed(outcome: null, error: "Sesión sin resultado interpretable.");
        }

        var readyResult = ApplyRefinement(number, response);
        if (readyResult.IsFailure)
        {
            logger.LogError("-> fallo actualizando el backlog para #{Number}: {Error}", number, readyResult.Error.Message);
            return RunOutcome.Failed(response.Outcome, error: readyResult.Error.Message);
        }

        logger.LogInformation(readyResult.Value ? "-> refinado: #{Number}" : "-> #{Number} sigue sin datos suficientes; queda a la espera de más información.", number);
        return RunOutcome.Succeeded(response.Outcome);
    }

    // Result<bool> rather than plain bool: the bool (was it marked "ready"?) is only
    // meaningful if both backlog writes actually went through — a Comment/Mark failure
    // (e.g. `gh` unavailable) must not be reported as "refined"/"missing-data" to the caller.
    private Result<bool> ApplyRefinement(int number, RefinerResponse response)
    {
        var comment = backlog.Comment(number, FormatRefinementComment(response));
        if (comment.IsFailure) return Result.Failure<bool>(comment.Error);

        if (response.Outcome == "ready")
            return backlog.MarkRefined(number).Bind(() => Result.Success(true));

        return backlog.MarkMissingData(number).Bind(() => Result.Success(false));
    }

    private static string FormatRefinementComment(RefinerResponse response)
    {
        var summary = string.IsNullOrWhiteSpace(response.Summary) ? "(sin resumen)" : response.Summary;
        if (response.Questions is not { Count: > 0 })
            return $"## Refinement\n\n{summary}";

        var questions = string.Join("\n", response.Questions.Select(q => $"- {q}"));
        return $"## Refinement\n\n{summary}\n\n### Open questions\n{questions}";
    }
}
