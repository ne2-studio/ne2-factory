using Ne2Factory.Cli.Agents;
using Ne2Factory.Cli.Common;

namespace Ne2Factory.Cli.Backlog;

// Applies a refiner's reported outcome to the backlog: posts its summary (and open
// questions, if any) as a comment on the ticket, then marks it refined or missing-data.
// The one place that turns a RefinementSignal into backlog state, shared by the
// unattended worker (AgentRunJobs) and the interactive `backlog refine` command.
internal static class RefinementOutcome
{
    public const string Ready = "ready";
    public const string MissingData = "missing_data";

    // Result<bool> rather than plain bool: the bool (was it marked "ready"?) is only
    // meaningful if both backlog writes actually went through — a Comment/Mark failure
    // (e.g. `gh` unavailable) must not be reported as "refined"/"missing-data" to the caller.
    public static Result<bool> Apply(IBacklog backlog, int number, RefinementSignal signal)
    {
        var comment = backlog.Comment(number, FormatComment(signal));
        if (comment.IsFailure) return Result.Failure<bool>(comment.Error);

        if (signal.Outcome == Ready)
            return backlog.MarkRefined(number).Bind(() => Result.Success(true));

        return backlog.MarkMissingData(number).Bind(() => Result.Success(false));
    }

    private static string FormatComment(RefinementSignal signal)
    {
        var summary = string.IsNullOrWhiteSpace(signal.Summary) ? "(sin resumen)" : signal.Summary;
        if (signal.Questions is not { Count: > 0 })
            return $"## Refinement\n\n{summary}";

        var questions = string.Join("\n", signal.Questions.Select(q => $"- {q}"));
        return $"## Refinement\n\n{summary}\n\n### Open questions\n{questions}";
    }
}
