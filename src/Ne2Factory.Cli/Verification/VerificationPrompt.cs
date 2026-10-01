using Ne2Factory.Cli.Backlog;
using Ne2Factory.Cli.FactoryWorker;

namespace Ne2Factory.Cli.Verification;

internal static class VerificationPrompt
{
    // The intent comes from the ticket the pull request resolves, not from the
    // pull request description: that one is the implementer's own account of
    // the change, so it's handed over as hints only.
    public static string For(PullRequest pr, BacklogItem? ticket, PullRequestCheckout checkout) =>
        PromptTemplates.Load("verify-pull-request")
            .Replace("{{PR_REFERENCE}}", $"#{pr.Number} ({pr.Url})")
            .Replace("{{INTENT}}", ticket is null ? pr.Title : FormatTicket(ticket))
            .Replace("{{BASE_BRANCH}}", pr.BaseBranch)
            .Replace("{{BASE_COMMIT}}", checkout.BaseCommit)
            .Replace("{{HEAD_COMMIT}}", checkout.HeadCommit)
            .Replace("{{PR_DESCRIPTION}}", string.IsNullOrWhiteSpace(pr.Body) ? "(none)" : pr.Body);

    private static string FormatTicket(BacklogItem ticket)
    {
        var comments = ticket.Comments.Count == 0 ? "" : "\n\n" + string.Join("\n", ticket.Comments);
        return $"Ticket #{ticket.Number}: {ticket.Title}\n\n{ticket.Body}{comments}";
    }
}
