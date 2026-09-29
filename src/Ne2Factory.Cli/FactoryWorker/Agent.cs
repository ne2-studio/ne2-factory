using Microsoft.Extensions.Logging;
using Ne2Factory.Cli.Agents;
using Ne2Factory.Cli.Backlog;

namespace Ne2Factory.Cli.FactoryWorker;

public class Agent(ICodingAgent codingAgent, IBacklog backlog, ILogger<Agent> logger)
{
    public AgentRunJobs.RunOutcome GenerateAndProcessResponse(string agentName, BacklogItem current)
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

    /// IMPLEMENTER
    ///
    private AgentRunJobs.RunOutcome PostWork(BacklogItem current, ImplementerResult response)
    {
        int number = current.Number;
        
        switch (response.Status)
        {
            case "done":
            {
                backlog.Comment(number, FormatWorkTicketComment(response));
                backlog.Close(number);

                logger.LogInformation("-> hecho: #{Number}", number);
                return AgentRunJobs.RunOutcome.Succeeded(response.Status);
            }
            case "blocked":
            {
                backlog.Comment(number, FormatWorkTicketComment(response));
                backlog.MarkFailed(number);

                logger.LogInformation("-> bloqueado: #{Number}{Reason}. Revisa y usa 'requeue {Number}' si procede.", number, string.IsNullOrEmpty(response.Reason) ? "" : $" ({response.Reason})", number);
                return AgentRunJobs.RunOutcome.Failed(response.Status, response.Reason);
            }
            default:
                logger.LogWarning("-> resultado desconocido ('{Status}'), marco #{Number} como fallido para revisión manual.", response.Status, number);
                backlog.MarkFailed(number);

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
    private AgentRunJobs.RunOutcome PostRefine(BacklogItem current, RefinerResult response)
    {
        var number = current.Number;
        var ready = response.Outcome == "ready";
        
        backlog.Comment(number, FormatRefinementComment(response));
        if (ready)
        {
            backlog.MarkRefined(number);
        }
        else
        {
            backlog.MarkMissingData(number);
        }
        
        logger.LogInformation(ready ? "-> refinado: #{Number}" : "-> #{Number} sigue sin datos suficientes; queda a la espera de más información.", number);
        return AgentRunJobs.RunOutcome.Succeeded(response.Outcome);
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