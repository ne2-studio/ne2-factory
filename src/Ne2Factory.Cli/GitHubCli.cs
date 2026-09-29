using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Ne2Factory.Cli.Common;
using Ne2Factory.Cli.Services;

namespace Ne2Factory.Cli;

internal sealed record IssueLabel([property: JsonPropertyName("name")] string Name);

internal sealed record IssueSummary(
    [property: JsonPropertyName("number")] int Number,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("labels")] IssueLabel[]? Labels = null,
    [property: JsonPropertyName("body")] string? Body = null,
    [property: JsonPropertyName("comments")] IssueComment[]? Comments = null);

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

internal interface IGitHub
{
    void CreateLabelSilently(string name, string color, string description);
    bool TryCreateLabel(string name, string color, string description, out string? error);
    Result<IssueSummary[]> ListIssues(IEnumerable<string> labels, string state, string fields);
    Result<string> CreateIssue(string label, string title, string body);
    Result EditIssueLabels(int number, string? removeLabel, string? addLabel);
    Result CloseIssue(int number);
    Result CommentOnIssue(int number, string body);
    IssueDetail? ViewIssue(int number, string fields);
}

// Wraps `gh` invocations. Every issue query goes through `--json` and is parsed
// here instead of relying on `gh --jq`, so behaviour doesn't depend on gh's
// bundled jq version — the filtering logic replicates the original `--jq` filters.
internal sealed class GitHub(IProcessRunner proc, ILogger<GitHub> logger) : IGitHub
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public void CreateLabelSilently(string name, string color, string description)
    {
        proc.Capture("gh", ["label", "create", name, "--color", color, "--description", description]);
    }

    public bool TryCreateLabel(string name, string color, string description, out string? error)
    {
        var (_, stderr, exit) = proc.Capture("gh", ["label", "create", name, "--color", color, "--description", description]);
        if (exit == 0 || stderr.Contains("already exists", StringComparison.OrdinalIgnoreCase))
        {
            error = null;
            return true;
        }
        error = stderr.Trim();
        return false;
    }

    public Result<IssueSummary[]> ListIssues(IEnumerable<string> labels, string state, string fields)
    {
        var args = new List<string> { "issue", "list" };
        foreach (var label in labels) { args.Add("--label"); args.Add(label); }
        args.Add("--state"); args.Add(state);
        args.Add("--json"); args.Add(fields);

        var (stdout, stderr, exit) = proc.Capture("gh", args);
        if (exit != 0)
        {
            logger.LogError("{Stderr}", stderr);
            return Result.Failure<IssueSummary[]>(ApplicationError.ExternalDependencyUnavailable(stderr.Trim()));
        }
        return Result.Success(JsonSerializer.Deserialize<IssueSummary[]>(stdout, JsonOptions) ?? []);
    }

    public Result<string> CreateIssue(string label, string title, string body)
    {
        var (stdout, stderr, exit) = proc.Capture("gh", ["issue", "create", "--label", label, "--title", title, "--body", body]);
        if (exit != 0)
        {
            logger.LogError("{Stderr}", stderr);
            return Result.Failure<string>(ApplicationError.ExternalDependencyUnavailable(stderr.Trim()));
        }
        return Result.Success(stdout.Trim());
    }

    public Result EditIssueLabels(int number, string? removeLabel, string? addLabel)
    {
        var args = new List<string> { "issue", "edit", number.ToString() };
        if (removeLabel is not null) { args.Add("--remove-label"); args.Add(removeLabel); }
        if (addLabel is not null) { args.Add("--add-label"); args.Add(addLabel); }
        var (_, stderr, exit) = proc.Capture("gh", args);
        if (exit != 0)
        {
            logger.LogError("{Stderr}", stderr);
            return Result.Failure(ApplicationError.ExternalDependencyUnavailable(stderr.Trim()));
        }
        return Result.Success();
    }

    public Result CloseIssue(int number)
    {
        var (_, stderr, exit) = proc.Capture("gh", ["issue", "close", number.ToString()]);
        if (exit != 0)
        {
            logger.LogError("{Stderr}", stderr);
            return Result.Failure(ApplicationError.ExternalDependencyUnavailable(stderr.Trim()));
        }
        return Result.Success();
    }

    public Result CommentOnIssue(int number, string body)
    {
        var (_, stderr, exit) = proc.Capture("gh", ["issue", "comment", number.ToString(), "--body", body]);
        if (exit != 0)
        {
            logger.LogError("{Stderr}", stderr);
            return Result.Failure(ApplicationError.ExternalDependencyUnavailable(stderr.Trim()));
        }
        return Result.Success();
    }

    public IssueDetail? ViewIssue(int number, string fields)
    {
        var (stdout, stderr, exit) = proc.Capture("gh", ["issue", "view", number.ToString(), "--json", fields]);
        if (exit != 0)
        {
            logger.LogError("{Stderr}", stderr);
            return null;
        }
        return JsonSerializer.Deserialize<IssueDetail>(stdout, JsonOptions);
    }
}
