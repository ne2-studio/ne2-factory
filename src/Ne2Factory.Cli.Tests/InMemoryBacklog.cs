using Ne2Factory.Cli.Backlog;
using Ne2Factory.Cli.Common;

namespace Ne2Factory.Cli.Tests;

internal sealed class InMemoryBacklog : IBacklog
{
    private readonly Dictionary<int, BacklogItem> items = [];

    // Injectable failures for the "gh unavailable" paths a plain in-memory item can't
    // reproduce on its own; leave null for the write to actually apply to the item.
    public ApplicationError? GetItemFailure { get; set; }
    public ApplicationError? CommentFailure { get; set; }
    public ApplicationError? CloseFailure { get; set; }
    public ApplicationError? MarkFailedFailure { get; set; }
    public ApplicationError? MarkRefinedFailure { get; set; }
    public ApplicationError? MarkMissingDataFailure { get; set; }
    public ApplicationError? MarkInReviewFailure { get; set; }

    public void Add(BacklogItem item) => items[item.Number] = item;

    public BacklogItem? Get(int number) => items.GetValueOrDefault(number);

    public void EnsureLabels() =>
        throw new NotSupportedException($"{nameof(InMemoryBacklog)} no soporta {nameof(EnsureLabels)}.");

    public IReadOnlyList<BacklogItem> ListPending() =>
        items.Values
            .Where(i => i.State is TicketState.Unrefined or TicketState.Refined)
            .ToArray();

    public IReadOnlyList<BacklogItem> ListUnrefined() =>
        throw new NotSupportedException($"{nameof(InMemoryBacklog)} no soporta {nameof(ListUnrefined)}.");

    public IReadOnlyList<BacklogItem> ListRefined() =>
        throw new NotSupportedException($"{nameof(InMemoryBacklog)} no soporta {nameof(ListRefined)}.");

    public IReadOnlyList<BacklogItem> ListMissingData() =>
        throw new NotSupportedException($"{nameof(InMemoryBacklog)} no soporta {nameof(ListMissingData)}.");

    public IReadOnlyList<BacklogItem> ListInReview() =>
        throw new NotSupportedException($"{nameof(InMemoryBacklog)} no soporta {nameof(ListInReview)}.");

    public IReadOnlyList<BacklogItem> ListDone() =>
        throw new NotSupportedException($"{nameof(InMemoryBacklog)} no soporta {nameof(ListDone)}.");

    public IReadOnlyList<BacklogItem> ListFailed() =>
        throw new NotSupportedException($"{nameof(InMemoryBacklog)} no soporta {nameof(ListFailed)}.");

    public BacklogItem? GetItem(int number) =>
        GetItemFailure is { } error ? throw new BacklogException(error) : Get(number);

    public void Requeue(int number) =>
        throw new NotSupportedException($"{nameof(InMemoryBacklog)} no soporta {nameof(Requeue)}.");

    public void Close(int number) => ApplyState(number, CloseFailure, TicketState.Done);

    public void MarkFailed(int number) => ApplyState(number, MarkFailedFailure, TicketState.Failed);

    public void MarkRefined(int number) => ApplyState(number, MarkRefinedFailure, TicketState.Refined);

    public void MarkMissingData(int number) => ApplyState(number, MarkMissingDataFailure, TicketState.MissingData);

    public void MarkInReview(int number) => ApplyState(number, MarkInReviewFailure, TicketState.InReview);

    public void Comment(int number, string body)
    {
        if (CommentFailure is { } error) throw new BacklogException(error);

        if (items.TryGetValue(number, out var item))
            items[number] = item with { Comments = [.. item.Comments, body] };
    }

    private void ApplyState(int number, ApplicationError? failure, TicketState state)
    {
        if (failure is { } error) throw new BacklogException(error);

        if (items.TryGetValue(number, out var item))
            items[number] = item with { State = state };
    }
}
