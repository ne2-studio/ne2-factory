using Microsoft.Extensions.Logging;
using Ne2Factory.Cli.Agents;
using Ne2Factory.Cli.Backlog;
using Ne2Factory.Cli.Common;

namespace Ne2Factory.Cli.FactoryWorker;

public class Agent(ICodingAgent codingAgent, IBacklog backlog, ILogger<Agent> logger)
{
    public AgentRunJobs.RunOutcome GenerateAndProcessResponse(string agentName, BacklogItem current, int number)
    {
        var prompt = agentName == TicketAgents.Refine
            ? PromptTemplates.RefineTicket(current)
            : PromptTemplates.WorkTicket(current);

        object? response = agentName == TicketAgents.Refine
            ? codingAgent.RunWithStructuredOutput<RefinerResult>(prompt, new CodingAgentOptions { SkipPermissions = true, Agent = TicketAgents.Refine }).Result
            : codingAgent.RunWithStructuredOutput<ImplementerResult>(prompt, new CodingAgentOptions { SkipPermissions = true, Agent = TicketAgents.Work }).Result;
        
        if (response is null)
        {
            return AgentRunJobs.RunOutcome.Failed(outcome: null, error: "Sesión sin resultado interpretable");
        }
        else
        {
            var outcome = agentName == TicketAgents.Refine
                ? PostRefine(current, (RefinerResult)response)
                : PostWork(current, (ImplementerResult)response);

            return outcome;
        }
    }
    
    private void LogIfFailed(Result result, int number)
    {
        if (result.IsFailure)
            logger.LogError("-> fallo actualizando el backlog para #{Number}: {Error}", number, result.Error.Message);
    }
    
    /// IMPLEMENTER
    ///
    private AgentRunJobs.RunOutcome PostWork(BacklogItem current, ImplementerResult response)
    {
        int number = current.Number;
        
        switch (response.Status)
        {
            case "done":
            {
                var comment = backlog.Comment(number, FormatWorkTicketComment(response));
                var close = comment.IsSuccess ? backlog.Close(number) : comment;
                if (close.IsFailure)
                {
                    logger.LogError("-> fallo actualizando el backlog para #{Number}: {Error}", number, close.Error.Message);
                    return AgentRunJobs.RunOutcome.Failed(response.Status, error: close.Error.Message);
                }
                logger.LogInformation("-> hecho: #{Number}", number);
                return AgentRunJobs.RunOutcome.Succeeded(response.Status);
            }
            case "blocked":
            {
                var comment = backlog.Comment(number, FormatWorkTicketComment(response));
                var markFailed = comment.IsSuccess ? backlog.MarkFailed(number) : comment;
                if (markFailed.IsFailure)
                    logger.LogError("-> fallo actualizando el backlog para #{Number}: {Error}", number, markFailed.Error.Message);
                logger.LogInformation("-> bloqueado: #{Number}{Reason}. Revisa y usa 'requeue {Number}' si procede.", number, string.IsNullOrEmpty(response.Reason) ? "" : $" ({response.Reason})", number);
                return AgentRunJobs.RunOutcome.Failed(response.Status, response.Reason);
            }
            default:
                logger.LogWarning("-> resultado desconocido ('{Status}'), marco #{Number} como fallido para revisión manual.", response.Status, number);
                LogIfFailed(backlog.MarkFailed(number), number);
                return AgentRunJobs.RunOutcome.Failed(response.Status, error: $"Resultado desconocido: {response.Status}");
        }
    }
    
    private static string FormatWorkTicketComment(ImplementerResult result)
    {
        var body = !string.IsNullOrWhiteSpace(result.Summary) ? result.Summary : result.Reason;
        return $"## {(result.Status == "done" ? "Done" : "Blocked")}\n\n{body ?? "(sin resumen)"}";
    }
    
    /// REFINER
    ///
    public AgentRunJobs.RunOutcome PostRefine(BacklogItem current, RefinerResult response)
    {
        var number = current.Number;
        
        var comment = backlog.Comment(number, FormatRefinementComment(response));
        
        Result<bool> readyResult;
        if (comment.IsFailure)
        {
            readyResult = Result.Failure<bool>(comment.Error);
        }
        else if (response.Outcome == "ready")
        {
            readyResult = backlog.MarkRefined(number).Bind(() => Result.Success(true));
        }
        else
        {
            readyResult = backlog.MarkMissingData(number).Bind(() => Result.Success(false));
        }

        if (readyResult.IsFailure)
        {
            logger.LogError("-> fallo actualizando el backlog para #{Number}: {Error}", number, readyResult.Error.Message);
            return AgentRunJobs.RunOutcome.Failed(response.Outcome, error: readyResult.Error.Message);
        }
        else
        {
            logger.LogInformation(readyResult.Value ? "-> refinado: #{Number}" : "-> #{Number} sigue sin datos suficientes; queda a la espera de más información.", number);
            return AgentRunJobs.RunOutcome.Succeeded(response.Outcome);            
        }
    }

    // Result<bool> rather than plain bool: the bool (was it marked "ready"?) is only
    // meaningful if both backlog writes actually went through — a Comment/Mark failure
    // (e.g. `gh` unavailable) must not be reported as "refined"/"missing-data" to the caller.

    private static string FormatRefinementComment(RefinerResult result)
    {
        var summary = string.IsNullOrWhiteSpace(result.Summary) ? "(sin resumen)" : result.Summary;
        if (result.Questions is not { Count: > 0 })
            return $"## Refinement\n\n{summary}";

        var questions = string.Join("\n", result.Questions.Select(q => $"- {q}"));
        return $"## Refinement\n\n{summary}\n\n### Open questions\n{questions}";
    }
}