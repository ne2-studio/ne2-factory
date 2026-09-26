namespace Ne2Factory.Cli.Backlog;

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
