namespace Ne2Factory.Cli.Backlog;

// Every read/write that can fail against the backing store (a `gh` call, for
// GithubIssuesBacklog) throws BacklogException so a genuine failure — network
// down, `gh` not authenticated, rate-limited — can't be confused with "no
// items" or "write succeeded". Callers decide what a failure means for them
// (skip this tick, mark a run Failed, print an error and exit 1, ...).
public interface IBacklog
{
    void EnsureLabels();

    // Tickets the factory can currently act on: refined (ready for the
    // implementer) or unrefined (ready for the refiner).
    IReadOnlyList<BacklogItem> ListPending();
    IReadOnlyList<BacklogItem> ListUnrefined();
    IReadOnlyList<BacklogItem> ListRefined();
    IReadOnlyList<BacklogItem> ListMissingData();
    IReadOnlyList<BacklogItem> ListInReview();
    IReadOnlyList<BacklogItem> ListDone();
    IReadOnlyList<BacklogItem> ListFailed();
    BacklogItem? GetItem(int number);
    void Requeue(int number);
    void Close(int number);
    void MarkFailed(int number);
    void MarkRefined(int number);
    void MarkMissingData(int number);
    // Implemented and handed over for review; closing it is up to the reviewer
    // (for GitHub, merging the pull request).
    void MarkInReview(int number);
    void Comment(int number, string body);
}
