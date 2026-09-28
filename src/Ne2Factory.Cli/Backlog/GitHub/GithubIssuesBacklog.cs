using Ne2Factory.Cli;
using Ne2Factory.Cli.Common;

namespace Ne2Factory.Cli.Backlog;

// Backs the backlog with GitHub issues: queue state lives in labels, ticket
// context comes from the issue body/comments. Only place that maps
// IGitHubCli's issue shape onto the backlog domain — and the only place that
// knows about labels at all; everything else works with TicketState.
//
// IGitHubCli surfaces `gh` failures as Result<T> instead of crashing the
// process; this class just forwards that Result (mapped into BacklogItem
// shapes) rather than unwrapping it, so a genuine `gh` failure can't be
// confused with "no issues" by whoever calls IBacklog.
internal sealed class GithubIssuesBacklog(IGitHubCli gitHubCli) : IBacklog
{
    private const string QueueLabel = "backlog";
    private const string RefinedLabel = "refined";
    private const string MissingDataLabel = "missing-data";
    private const string FailedLabel = "backlog:failed";

    public void EnsureLabels()
    {
        gitHubCli.CreateLabelSilently(QueueLabel, "0E8A16", "Backlog ticket queued for bin/backlog");
        gitHubCli.CreateLabelSilently(RefinedLabel, "0E8A16", "Ticket refinado, listo para bin/backlog");
        gitHubCli.CreateLabelSilently(MissingDataLabel, "FBCA04", "Ticket refinement stalled, needs more info from the reviewer");
        gitHubCli.CreateLabelSilently(FailedLabel, "B60205", "Backlog ticket blocked, needs review before requeuing");
    }

    public Result<IReadOnlyList<BacklogItem>> ListPending() =>
        gitHubCli.ListIssues([QueueLabel], "open", "number,title,labels")
            .Map(issues => (IReadOnlyList<BacklogItem>)issues
                .Where(i => i.Labels?.Any(l => l.Name == MissingDataLabel) != true)
                .Select(i => ToBacklogItem(i, i.Labels?.Any(l => l.Name == RefinedLabel) == true ? TicketState.Refined : TicketState.Unrefined))
                .ToArray());

    public Result<IReadOnlyList<BacklogItem>> ListUnrefined() =>
        gitHubCli.ListIssues([QueueLabel], "open", "number,title,labels")
            .Map(issues => (IReadOnlyList<BacklogItem>)issues
                .Where(i => i.Labels?.Any(l => l.Name is RefinedLabel or MissingDataLabel) != true)
                .Select(i => ToBacklogItem(i, TicketState.Unrefined))
                .ToArray());

    public Result<IReadOnlyList<BacklogItem>> ListRefined() =>
        gitHubCli.ListIssues([QueueLabel, RefinedLabel], "open", "number,title")
            .Map(issues => (IReadOnlyList<BacklogItem>)issues.Select(i => ToBacklogItem(i, TicketState.Refined)).ToArray());

    public Result<IReadOnlyList<BacklogItem>> ListMissingData() =>
        gitHubCli.ListIssues([QueueLabel, MissingDataLabel], "open", "number,title")
            .Map(issues => (IReadOnlyList<BacklogItem>)issues.Select(i => ToBacklogItem(i, TicketState.MissingData)).ToArray());

    public Result<IReadOnlyList<BacklogItem>> ListDone() =>
        gitHubCli.ListIssues([QueueLabel], "closed", "number,title")
            .Map(issues => (IReadOnlyList<BacklogItem>)issues.Select(i => ToBacklogItem(i, TicketState.Done)).ToArray());

    public Result<IReadOnlyList<BacklogItem>> ListFailed() =>
        gitHubCli.ListIssues([FailedLabel], "open", "number,title")
            .Map(issues => (IReadOnlyList<BacklogItem>)issues.Select(i => ToBacklogItem(i, TicketState.Failed)).ToArray());

    public Result<BacklogItem?> GetItem(int number)
    {
        var issue = gitHubCli.ViewIssue(number, "title,body,url,state,labels,comments");
        if (issue is null) return Result.Success<BacklogItem?>(null);

        var comments = (issue.Comments ?? [])
            .Select(c => $"-- comment by {c.Author.Login} ({c.CreatedAt}) --\n{c.Body}")
            .ToArray();

        var labels = issue.Labels?.Select(l => l.Name).ToHashSet() ?? new HashSet<string>();
        return Result.Success<BacklogItem?>(new BacklogItem(
            number,
            issue.Title,
            issue.Body,
            issue.Url,
            ToTicketState(issue.State, labels),
            comments));
    }

    public Result Requeue(int number)
    {
        var removeFailed = gitHubCli.EditIssueLabels(number, FailedLabel, QueueLabel);
        return removeFailed.IsFailure ? removeFailed : gitHubCli.EditIssueLabels(number, MissingDataLabel, null);
    }

    public Result Close(int number) => gitHubCli.CloseIssue(number);

    public Result MarkFailed(int number) => gitHubCli.EditIssueLabels(number, QueueLabel, FailedLabel);

    public Result MarkRefined(int number) => gitHubCli.EditIssueLabels(number, MissingDataLabel, RefinedLabel);

    public Result MarkMissingData(int number) => gitHubCli.EditIssueLabels(number, null, MissingDataLabel);

    public Result Comment(int number, string body) => gitHubCli.CommentOnIssue(number, body);

    private static BacklogItem ToBacklogItem(IssueSummary i, TicketState state) =>
        new(i.Number, i.Title, null, null, state, []);

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
        if (queued && labels.Contains(RefinedLabel)) return TicketState.Refined;
        if (queued && labels.Contains(MissingDataLabel)) return TicketState.MissingData;
        if (queued) return TicketState.Unrefined;
        return TicketState.Unknown;
    }
}
