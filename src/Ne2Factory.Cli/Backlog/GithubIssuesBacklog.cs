using Ne2Factory.Cli;

namespace Ne2Factory.Cli.Backlog;

// A ticket's state as the factory understands it. Computed from whatever the
// concrete IBacklog implementation uses internally (GitHub labels, for
// GithubIssuesBacklog) — the factory domain never sees labels directly.
internal enum TicketState
{
    Unrefined,
    Refined,
    Failed,
    Done,
    Unknown,
}

internal sealed record BacklogItem(
    int Number,
    string Title,
    string? Body,
    string? Url,
    TicketState State,
    IReadOnlyList<string> Comments);

internal interface IBacklog
{
    void EnsureLabels();
    IReadOnlyList<BacklogItem> ListUnrefined();
    IReadOnlyList<BacklogItem> ListRefined();
    IReadOnlyList<BacklogItem> ListDone();
    IReadOnlyList<BacklogItem> ListFailed();
    BacklogItem? GetItem(int number);
    void Requeue(int number);
    void Close(int number);
    void MarkFailed(int number);
}

// Backs the backlog with GitHub issues: queue state lives in labels, ticket
// context comes from the issue body/comments. Only place that maps
// IGitHubCli's issue shape onto the backlog domain — and the only place that
// knows about labels at all; everything else works with TicketState.
internal sealed class GithubIssuesBacklog(IGitHubCli gitHubCli) : IBacklog
{
    private const string QueueLabel = "backlog";
    private const string RefinedLabel = "refined";
    private const string FailedLabel = "backlog:failed";

    public void EnsureLabels()
    {
        gitHubCli.CreateLabelSilently(QueueLabel, "0E8A16", "Backlog ticket queued for bin/backlog");
        gitHubCli.CreateLabelSilently(RefinedLabel, "0E8A16", "Ticket refinado, listo para bin/backlog");
        gitHubCli.CreateLabelSilently(FailedLabel, "B60205", "Backlog ticket blocked, needs review before requeuing");
    }

    public IReadOnlyList<BacklogItem> ListUnrefined() =>
        gitHubCli.ListIssues([QueueLabel], "open", "number,title,labels")
            .Where(i => i.Labels?.Any(l => l.Name == RefinedLabel) != true)
            .Select(i => ToBacklogItem(i, TicketState.Unrefined))
            .ToArray();

    public IReadOnlyList<BacklogItem> ListRefined() =>
        gitHubCli.ListIssues([QueueLabel, RefinedLabel], "open", "number,title")
            .Select(i => ToBacklogItem(i, TicketState.Refined))
            .ToArray();

    public IReadOnlyList<BacklogItem> ListDone() =>
        gitHubCli.ListIssues([QueueLabel], "closed", "number,title")
            .Select(i => ToBacklogItem(i, TicketState.Done))
            .ToArray();

    public IReadOnlyList<BacklogItem> ListFailed() =>
        gitHubCli.ListIssues([FailedLabel], "open", "number,title")
            .Select(i => ToBacklogItem(i, TicketState.Failed))
            .ToArray();

    public BacklogItem? GetItem(int number)
    {
        var issue = gitHubCli.ViewIssue(number, "title,body,url,state,labels,comments");
        if (issue is null) return null;

        var comments = (issue.Comments ?? [])
            .Select(c => $"-- comment by {c.Author.Login} ({c.CreatedAt}) --\n{c.Body}")
            .ToArray();

        var labels = issue.Labels?.Select(l => l.Name).ToHashSet() ?? new HashSet<string>();
        return new BacklogItem(
            number,
            issue.Title,
            issue.Body,
            issue.Url,
            ToTicketState(issue.State, labels),
            comments);
    }

    public void Requeue(int number) => gitHubCli.EditIssueLabels(number, FailedLabel, QueueLabel);

    public void Close(int number) => gitHubCli.CloseIssue(number);

    public void MarkFailed(int number) => gitHubCli.EditIssueLabels(number, QueueLabel, FailedLabel);

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
        if (queued) return TicketState.Unrefined;
        return TicketState.Unknown;
    }
}
