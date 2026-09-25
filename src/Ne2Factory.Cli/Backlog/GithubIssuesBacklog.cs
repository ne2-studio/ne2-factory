using Ne2Factory.Cli;

namespace Ne2Factory.Cli.Backlog;

internal sealed record BacklogItem(
    int Number,
    string Title,
    string? Body,
    string? Url,
    string? State,
    IReadOnlySet<string> Labels,
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
// IGitHubCli's issue shape onto the backlog domain.
internal sealed class GithubIssuesBacklog(IGitHubCli gitHubCli) : IBacklog
{
    public const string QueueLabel = "backlog";
    public const string RefinedLabel = "refined";
    public const string FailedLabel = "backlog:failed";

    public void EnsureLabels()
    {
        gitHubCli.CreateLabelSilently(QueueLabel, "0E8A16", "Backlog ticket queued for bin/backlog");
        gitHubCli.CreateLabelSilently(RefinedLabel, "0E8A16", "Ticket refinado, listo para bin/backlog");
        gitHubCli.CreateLabelSilently(FailedLabel, "B60205", "Backlog ticket blocked, needs review before requeuing");
    }

    public IReadOnlyList<BacklogItem> ListUnrefined() =>
        gitHubCli.ListIssues([QueueLabel], "open", "number,title,labels")
            .Where(i => i.Labels?.Any(l => l.Name == RefinedLabel) != true)
            .Select(ToBacklogItem)
            .ToArray();

    public IReadOnlyList<BacklogItem> ListRefined() =>
        gitHubCli.ListIssues([QueueLabel, RefinedLabel], "open", "number,title")
            .Select(ToBacklogItem)
            .ToArray();

    public IReadOnlyList<BacklogItem> ListDone() =>
        gitHubCli.ListIssues([QueueLabel], "closed", "number,title")
            .Select(ToBacklogItem)
            .ToArray();

    public IReadOnlyList<BacklogItem> ListFailed() =>
        gitHubCli.ListIssues([FailedLabel], "open", "number,title")
            .Select(ToBacklogItem)
            .ToArray();

    public BacklogItem? GetItem(int number)
    {
        var issue = gitHubCli.ViewIssue(number, "title,body,url,state,labels,comments");
        if (issue is null) return null;

        var comments = (issue.Comments ?? [])
            .Select(c => $"-- comment by {c.Author.Login} ({c.CreatedAt}) --\n{c.Body}")
            .ToArray();

        return new BacklogItem(
            number,
            issue.Title,
            issue.Body,
            issue.Url,
            issue.State,
            issue.Labels?.Select(l => l.Name).ToHashSet() ?? new HashSet<string>(),
            comments);
    }

    public void Requeue(int number) => gitHubCli.EditIssueLabels(number, FailedLabel, QueueLabel);

    public void Close(int number) => gitHubCli.CloseIssue(number);

    public void MarkFailed(int number) => gitHubCli.EditIssueLabels(number, QueueLabel, FailedLabel);

    private static BacklogItem ToBacklogItem(IssueSummary i) =>
        new(i.Number, i.Title, null, null, null, i.Labels?.Select(l => l.Name).ToHashSet() ?? new HashSet<string>(), []);
}
