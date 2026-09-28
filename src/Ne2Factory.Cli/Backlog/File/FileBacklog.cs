using System.Globalization;
using Ne2Factory.Cli.Common;

namespace Ne2Factory.Cli.Backlog;

// Backs the backlog with plain text files under ProjectContext.BacklogDir:
// one subfolder per ticket, named after its number, holding a `ticket.txt`
// (State/Title header + body) and an optional `comments/` folder. No GitHub,
// no labels — this is the only place that knows about the on-disk layout;
// everything else works with BacklogItem/TicketState. Ticket creation is
// external (a human creates the folder), same as GithubIssuesBacklog expects
// issues to be filed directly on GitHub.
internal sealed class FileBacklog(ProjectContext ctx) : IBacklog
{
    private const string TicketFileName = "ticket.txt";
    private const string CommentsDirName = "comments";

    public void EnsureLabels() => Directory.CreateDirectory(ctx.BacklogDir);

    public Result<IReadOnlyList<BacklogItem>> ListPending() => Result.Success(ListByStates(s => s is TicketState.Unrefined or TicketState.Refined));
    public Result<IReadOnlyList<BacklogItem>> ListUnrefined() => Result.Success(ListByState(TicketState.Unrefined));
    public Result<IReadOnlyList<BacklogItem>> ListRefined() => Result.Success(ListByState(TicketState.Refined));
    public Result<IReadOnlyList<BacklogItem>> ListMissingData() => Result.Success(ListByState(TicketState.MissingData));
    public Result<IReadOnlyList<BacklogItem>> ListDone() => Result.Success(ListByState(TicketState.Done));
    public Result<IReadOnlyList<BacklogItem>> ListFailed() => Result.Success(ListByState(TicketState.Failed));

    public Result<BacklogItem?> GetItem(int number)
    {
        var dir = TicketDir(number);
        var ticketFile = Path.Combine(dir, TicketFileName);
        if (!File.Exists(ticketFile)) return Result.Success<BacklogItem?>(null);

        var (state, title, body) = ParseTicket(File.ReadAllText(ticketFile));
        return Result.Success<BacklogItem?>(new BacklogItem(number, title, body, Url: null, state, ReadComments(dir)));
    }

    public Result Requeue(int number) { SetState(number, TicketState.Unrefined); return Result.Success(); }
    public Result Close(int number) { SetState(number, TicketState.Done); return Result.Success(); }
    public Result MarkFailed(int number) { SetState(number, TicketState.Failed); return Result.Success(); }
    public Result MarkRefined(int number) { SetState(number, TicketState.Refined); return Result.Success(); }
    public Result MarkMissingData(int number) { SetState(number, TicketState.MissingData); return Result.Success(); }

    public Result Comment(int number, string body)
    {
        var commentsDir = Path.Combine(TicketDir(number), CommentsDirName);
        Directory.CreateDirectory(commentsDir);
        var index = Directory.GetFiles(commentsDir).Length;
        File.WriteAllText(Path.Combine(commentsDir, $"{index:0000}.txt"), body);
        return Result.Success();
    }

    private IReadOnlyList<BacklogItem> ListByState(TicketState state) => ListByStates(s => s == state);

    private IReadOnlyList<BacklogItem> ListByStates(Func<TicketState, bool> matches)
    {
        if (!Directory.Exists(ctx.BacklogDir)) return [];

        return Directory.GetDirectories(ctx.BacklogDir)
            .Select(dir => int.TryParse(Path.GetFileName(dir), out var number) ? number : (int?)null)
            .Where(number => number.HasValue)
            .Select(number => GetItem(number!.Value).Value)
            .Where(item => item is not null && matches(item.State))
            .Select(item => item!)
            .ToArray();
    }

    private void SetState(int number, TicketState state)
    {
        var ticketFile = Path.Combine(TicketDir(number), TicketFileName);
        var (_, title, body) = ParseTicket(File.ReadAllText(ticketFile));
        File.WriteAllText(ticketFile, FormatTicket(state, title, body));
    }

    private string TicketDir(int number) => Path.Combine(ctx.BacklogDir, number.ToString(CultureInfo.InvariantCulture));

    private static IReadOnlyList<string> ReadComments(string ticketDir)
    {
        var commentsDir = Path.Combine(ticketDir, CommentsDirName);
        if (!Directory.Exists(commentsDir)) return [];

        return Directory.GetFiles(commentsDir)
            .OrderBy(f => Path.GetFileName(f), StringComparer.Ordinal)
            .Select(File.ReadAllText)
            .ToArray();
    }

    // Header block of "Key: Value" lines up to the first blank line, then the
    // body verbatim. Unrecognized/missing State defaults to Unrefined, same
    // as a freshly filed ticket with no ticket.txt yet would mean.
    private static (TicketState State, string Title, string? Body) ParseTicket(string content)
    {
        var lines = content.Replace("\r\n", "\n").Split('\n');
        var state = TicketState.Unrefined;
        var title = "";

        var i = 0;
        for (; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.Length == 0) { i++; break; }

            var separator = line.IndexOf(':');
            if (separator < 0) continue;

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            if (key.Equals("State", StringComparison.OrdinalIgnoreCase) && Enum.TryParse<TicketState>(value, true, out var parsed))
                state = parsed;
            else if (key.Equals("Title", StringComparison.OrdinalIgnoreCase))
                title = value;
        }

        var body = i < lines.Length ? string.Join('\n', lines[i..]).Trim() : "";
        return (state, title, body.Length == 0 ? null : body);
    }

    private static string FormatTicket(TicketState state, string title, string? body) =>
        $"State: {state}\nTitle: {title}\n\n{body}";
}
