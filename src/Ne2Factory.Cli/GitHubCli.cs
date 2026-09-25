using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ne2Factory.Cli;

internal sealed record IssueLabel([property: JsonPropertyName("name")] string Name);

internal sealed record IssueSummary(
    [property: JsonPropertyName("number")] int Number,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("labels")] IssueLabel[]? Labels = null);

internal sealed record IssueComment(
    [property: JsonPropertyName("author")] IssueAuthor Author,
    [property: JsonPropertyName("createdAt")] string CreatedAt,
    [property: JsonPropertyName("body")] string Body);

internal sealed record IssueAuthor([property: JsonPropertyName("login")] string Login);

internal sealed record IssueDetail(
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("body")] string? Body,
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("comments")] IssueComment[]? Comments,
    [property: JsonPropertyName("state")] string? State,
    [property: JsonPropertyName("labels")] IssueLabel[]? Labels);

// Wraps `gh` invocations. Every issue query goes through `--json` and is parsed
// here instead of relying on `gh --jq`, so behaviour doesn't depend on gh's
// bundled jq version — the filtering logic replicates the original `--jq` filters.
internal static class GitHubCli
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static void CreateLabelSilently(string name, string color, string description)
    {
        Proc.Capture("gh", ["label", "create", name, "--color", color, "--description", description]);
    }

    public static bool TryCreateLabel(string name, string color, string description, out string? error)
    {
        var (_, stderr, exit) = Proc.Capture("gh", ["label", "create", name, "--color", color, "--description", description]);
        if (exit == 0 || stderr.Contains("already exists", StringComparison.OrdinalIgnoreCase))
        {
            error = null;
            return true;
        }
        error = stderr.Trim();
        return false;
    }

    public static IssueSummary[] ListIssues(IEnumerable<string> labels, string state, string fields)
    {
        var args = new List<string> { "issue", "list" };
        foreach (var label in labels) { args.Add("--label"); args.Add(label); }
        args.Add("--state"); args.Add(state);
        args.Add("--json"); args.Add(fields);

        var (stdout, stderr, exit) = Proc.Capture("gh", args);
        if (exit != 0)
        {
            Console.Error.WriteLine(stderr);
            Environment.Exit(1);
        }
        return JsonSerializer.Deserialize<IssueSummary[]>(stdout, JsonOptions) ?? [];
    }

    public static string CreateIssue(string label, string title, string body)
    {
        var (stdout, stderr, exit) = Proc.Capture("gh", ["issue", "create", "--label", label, "--title", title, "--body", body]);
        if (exit != 0)
        {
            Console.Error.WriteLine(stderr);
            Environment.Exit(1);
        }
        return stdout.Trim();
    }

    public static void EditIssueLabels(int number, string? removeLabel, string? addLabel)
    {
        var args = new List<string> { "issue", "edit", number.ToString() };
        if (removeLabel is not null) { args.Add("--remove-label"); args.Add(removeLabel); }
        if (addLabel is not null) { args.Add("--add-label"); args.Add(addLabel); }
        var (_, stderr, exit) = Proc.Capture("gh", args);
        if (exit != 0)
        {
            Console.Error.WriteLine(stderr);
            Environment.Exit(1);
        }
    }

    public static void CloseIssue(int number)
    {
        var (_, stderr, exit) = Proc.Capture("gh", ["issue", "close", number.ToString()]);
        if (exit != 0)
        {
            Console.Error.WriteLine(stderr);
            Environment.Exit(1);
        }
    }

    public static IssueDetail? ViewIssue(int number, string fields)
    {
        var (stdout, stderr, exit) = Proc.Capture("gh", ["issue", "view", number.ToString(), "--json", fields]);
        if (exit != 0)
        {
            Console.Error.WriteLine(stderr);
            return null;
        }
        return JsonSerializer.Deserialize<IssueDetail>(stdout, JsonOptions);
    }
}
