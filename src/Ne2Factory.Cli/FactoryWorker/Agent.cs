using Microsoft.Extensions.Logging;
using Ne2Factory.Cli.Agents;
using Ne2Factory.Cli.Backlog;
using Ne2Factory.Cli.Common;

namespace Ne2Factory.Cli.FactoryWorker;

public class Agent(ICodingAgent codingAgent, IBacklog backlog, TicketWorkspace workspace, ILogger<Agent> logger)
{
    public Result<string> GenerateAndProcessResponse(string agentName, BacklogItem current, string baseBranch)
    {
        var prompt = agentName == TicketAgents.Refine
            ? PromptTemplates.RefineTicket(current)
            : PromptTemplates.WorkTicket(current);

        object? result = agentName == TicketAgents.Refine
            ? codingAgent.RunWithStructuredOutput<RefinerResult>(prompt, new CodingAgentOptions { SkipPermissions = true, Agent = TicketAgents.Refine }).Result
            : codingAgent.RunWithStructuredOutput<ImplementerResult>(prompt, new CodingAgentOptions { SkipPermissions = true, Agent = TicketAgents.Work }).Result;

        if (result is null)
        {
            return Result.Failure<string>(ApplicationError.ExternalDependencyUnavailable("Sesión sin resultado interpretable"));
        }
        else
        {
            var outcome = agentName == TicketAgents.Refine
                ? PostRefine(current, (RefinerResult)result)
                : PostWork(current, (ImplementerResult)result, baseBranch);

            return outcome;
        }
    }

    ///
    /// IMPLEMENTER
    ///

    private Result<string> PostWork(BacklogItem current, ImplementerResult result, string baseBranch)
    {
        int number = current.Number;
        
        switch (result.Status)
        {
            case "done":
            {
                var delivered = workspace.Deliver(current, baseBranch, result.Summary);
                if (delivered.IsFailure)
                {
                    backlog.Comment(number, FormatDeliveryFailedComment(result, delivered.Error));
                    return Result.Failure<string>(delivered.Error);
                }

                backlog.Comment(number, FormatInReviewComment(result, delivered.Value));
                backlog.MarkInReview(number);

                logger.LogInformation("-> en revisión: #{Number} ({Reference})", number, delivered.Value);
                return result.Status;
            }
            case "blocked":
            {
                backlog.Comment(number, FormatBlockedComment(result));
                backlog.MarkFailed(number);

                logger.LogInformation("-> bloqueado: #{Number}{Reason}. Revisa y usa 'requeue {Number}' si procede.", number, string.IsNullOrEmpty(result.Reason) ? "" : $" ({result.Reason})", number);
                return result.Status;
            }
            default:
                logger.LogWarning("-> resultado desconocido ('{Status}'), marco #{Number} como fallido para revisión manual.", result.Status, number);
                backlog.MarkFailed(number);

                return Result.Failure<string>(ApplicationError.ExternalDependencyUnavailable($"Resultado desconocido: {result.Status}"));
        }
    }
    
    private static string FormatInReviewComment(ImplementerResult result, string reference) =>
        $"## In review\n\n{reference}\n\n{SummaryOrPlaceholder(result.Summary)}";

    private static string FormatDeliveryFailedComment(ImplementerResult result, ApplicationError error) =>
        $"## Delivery failed\n\n{error.Message}\n\n{SummaryOrPlaceholder(result.Summary)}";

    private static string FormatBlockedComment(ImplementerResult result) =>
        $"## Blocked\n\n{SummaryOrPlaceholder(result.Reason)}";

    private static string SummaryOrPlaceholder(string? text) =>
        string.IsNullOrWhiteSpace(text) ? "(sin resumen)" : text;
    
    ///
    /// REFINER
    ///

    private Result<string> PostRefine(BacklogItem current, RefinerResult result)
    {
        var number = current.Number;
        var ready = result.Outcome == "ready";
        
        backlog.Comment(number, FormatRefinementComment(result));
        if (ready)
        {
            backlog.MarkRefined(number);
        }
        else
        {
            backlog.MarkMissingData(number);
        }
        
        logger.LogInformation(ready ? "-> refinado: #{Number}" : "-> #{Number} sigue sin datos suficientes; queda a la espera de más información.", number);
        return result.Outcome;
    }

    private static string FormatRefinementComment(RefinerResult result)
    {
        var summary = string.IsNullOrWhiteSpace(result.Summary) ? "(sin resumen)" : result.Summary;
        if (result.Questions is not { Count: > 0 })
            return $"## Refinement\n\n{summary}";

        var questions = string.Join("\n", result.Questions.Select(q => $"- {q}"));
        return $"## Refinement\n\n{summary}\n\n### Open questions\n{questions}";
    }
}