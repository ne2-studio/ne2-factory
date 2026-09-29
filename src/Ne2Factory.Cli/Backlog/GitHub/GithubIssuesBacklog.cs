using Ne2Factory.Cli.Common;

namespace Ne2Factory.Cli.Backlog.GitHub;

// Backs the backlog with GitHub issues: queue state lives in labels, ticket
// context comes from the issue body/comments. Only place that maps
// IGitHubCli's issue shape onto the backlog domain — and the only place that
// knows about labels at all; everything else works with TicketState.
//
// IGitHub surfaces `gh` failures as Result<T>; this class turns them into
// BacklogException so a genuine `gh` failure can't be confused with "no
// issues" by whoever calls IBacklog.
internal sealed class GithubIssuesBacklog(IGitHub gitHub) : IBacklog
{
    private const string QueueLabel = "backlog";
    private const string RefinedLabel = "refined";
    private const string MissingDataLabel = "missing-data";
    private const string InReviewLabel = "in-review";
    private const string FailedLabel = "backlog:failed";

    public void EnsureLabels()
    {
        gitHub.CreateLabelSilently(QueueLabel, "0E8A16", "Backlog ticket queued for bin/backlog");
        gitHub.CreateLabelSilently(RefinedLabel, "0E8A16", "Ticket refinado, listo para bin/backlog");
        gitHub.CreateLabelSilently(MissingDataLabel, "FBCA04", "Ticket refinement stalled, needs more info from the reviewer");
        gitHub.CreateLabelSilently(InReviewLabel, "1D76DB", "Ticket implementado, pull request pendiente de revisión");
        gitHub.CreateLabelSilently(FailedLabel, "B60205", "Backlog ticket blocked, needs review before requeuing");
    }

    public IReadOnlyList<BacklogItem> ListPending() =>
        Unwrap(gitHub.ListIssues([QueueLabel], "open", "number,title,labels,body,comments"))
            .Where(i => i.Labels?.Any(l => l.Name is MissingDataLabel or InReviewLabel) != true)
            .Select(i => ToBacklogItem(
                i,
                i.Labels?.Any(l => l.Name == RefinedLabel) == true ? TicketState.Refined : TicketState.Unrefined,
                i.Body,
                FormatComments(i.Comments)))
            .ToArray();

    public IReadOnlyList<BacklogItem> ListUnrefined() =>
        Unwrap(gitHub.ListIssues([QueueLabel], "open", "number,title,labels"))
            .Where(i => i.Labels?.Any(l => l.Name is RefinedLabel or MissingDataLabel) != true)
            .Select(i => ToBacklogItem(i, TicketState.Unrefined))
            .ToArray();

    public IReadOnlyList<BacklogItem> ListRefined() =>
        Unwrap(gitHub.ListIssues([QueueLabel, RefinedLabel], "open", "number,title,labels"))
            .Where(i => i.Labels?.Any(l => l.Name == InReviewLabel) != true)
            .Select(i => ToBacklogItem(i, TicketState.Refined)).ToArray();

    public IReadOnlyList<BacklogItem> ListMissingData() =>
        Unwrap(gitHub.ListIssues([QueueLabel, MissingDataLabel], "open", "number,title"))
            .Select(i => ToBacklogItem(i, TicketState.MissingData)).ToArray();

    public IReadOnlyList<BacklogItem> ListInReview() =>
        Unwrap(gitHub.ListIssues([QueueLabel, InReviewLabel], "open", "number,title"))
            .Select(i => ToBacklogItem(i, TicketState.InReview)).ToArray();

    public IReadOnlyList<BacklogItem> ListDone() =>
        Unwrap(gitHub.ListIssues([QueueLabel], "closed", "number,title"))
            .Select(i => ToBacklogItem(i, TicketState.Done)).ToArray();

    public IReadOnlyList<BacklogItem> ListFailed() =>
        Unwrap(gitHub.ListIssues([FailedLabel], "open", "number,title"))
            .Select(i => ToBacklogItem(i, TicketState.Failed)).ToArray();

    public BacklogItem? GetItem(int number)
    {
        var issue = gitHub.ViewIssue(number, "title,body,url,state,labels,comments");
        if (issue is null) return null;

        var comments = FormatComments(issue.Comments);
        var labels = issue.Labels?.Select(l => l.Name).ToHashSet() ?? new HashSet<string>();
        return new BacklogItem(
            number,
            issue.Title,
            issue.Body,
            issue.Url,
            ToTicketState(issue.State, labels),
            comments);
    }

    public void Requeue(int number)
    {
        Unwrap(gitHub.EditIssueLabels(number, FailedLabel, QueueLabel));
        Unwrap(gitHub.EditIssueLabels(number, MissingDataLabel, null));
        Unwrap(gitHub.EditIssueLabels(number, InReviewLabel, null));
    }

    public void Close(int number) => Unwrap(gitHub.CloseIssue(number));

    public void MarkFailed(int number) => Unwrap(gitHub.EditIssueLabels(number, QueueLabel, FailedLabel));

    public void MarkRefined(int number) => Unwrap(gitHub.EditIssueLabels(number, MissingDataLabel, RefinedLabel));

    public void MarkMissingData(int number) => Unwrap(gitHub.EditIssueLabels(number, null, MissingDataLabel));

    public void MarkInReview(int number) => Unwrap(gitHub.EditIssueLabels(number, null, InReviewLabel));

    public void Comment(int number, string body) => Unwrap(gitHub.CommentOnIssue(number, body));

    private static T Unwrap<T>(Result<T> result) =>
        result.IsSuccess ? result.Value : throw new BacklogException(result.Error);

    private static void Unwrap(Result result)
    {
        if (result.IsFailure) throw new BacklogException(result.Error);
    }

    private static BacklogItem ToBacklogItem(IssueSummary i, TicketState state, string? body = null, IReadOnlyList<string>? comments = null) =>
        new(i.Number, i.Title, body, null, state, comments ?? []);

    private static IReadOnlyList<string> FormatComments(IssueComment[]? comments) =>
        (comments ?? [])
            .Select(c => $"-- comment by {c.Author.Login} ({c.CreatedAt}) --\n{c.Body}")
            .ToArray();

    // Mirrors the label combinations the List* methods above filter by, for
    // issues fetched individually (GetItem) where the state isn't already
    // known from the query that produced them.
    private static TicketState ToTicketState(string? issueState, IReadOnlySet<string> labels)
    {
        var queued = labels.Contains(QueueLabel);
        if (string.Equals(issueState, "closed", StringComparison.OrdinalIgnoreCase))
            return queued ? TicketState.Done : TicketState.Unknown;

        if (!string.Equals(issueState, "open", StringComparison.OrdinalIgnoreCase))
            return TicketState.Unknown;

        if (labels.Contains(FailedLabel)) return TicketState.Failed;
        if (queued && labels.Contains(InReviewLabel)) return TicketState.InReview;
        if (queued && labels.Contains(RefinedLabel)) return TicketState.Refined;
        if (queued && labels.Contains(MissingDataLabel)) return TicketState.MissingData;
        if (queued) return TicketState.Unrefined;
        return TicketState.Unknown;
    }
}
