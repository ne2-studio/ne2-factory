using Ne2Factory.Cli.Common;

namespace Ne2Factory.Cli.Backlog;

// Every read/write that can fail against the backing store (a `gh` call, for
// GithubIssuesBacklog) returns Result so a genuine failure — network down,
// `gh` not authenticated, rate-limited — can't be confused with "no items"
// or "write succeeded". Callers decide what a failure means for them
// (skip this tick, mark a run Failed, print an error and exit 1, ...).
internal interface IBacklog
{
    void EnsureLabels();
    Result<IReadOnlyList<BacklogItem>> ListUnrefined();
    Result<IReadOnlyList<BacklogItem>> ListRefined();
    Result<IReadOnlyList<BacklogItem>> ListMissingData();
    Result<IReadOnlyList<BacklogItem>> ListDone();
    Result<IReadOnlyList<BacklogItem>> ListFailed();
    Result<BacklogItem?> GetItem(int number);
    Result Requeue(int number);
    Result Close(int number);
    Result MarkFailed(int number);
    Result MarkRefined(int number);
    Result MarkMissingData(int number);
    Result Comment(int number, string body);
}
