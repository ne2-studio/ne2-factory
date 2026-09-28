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

    public void Add(BacklogItem item) => items[item.Number] = item;

    public BacklogItem? Get(int number) => items.GetValueOrDefault(number);

    public void EnsureLabels() =>
        throw new NotSupportedException($"{nameof(InMemoryBacklog)} no soporta {nameof(EnsureLabels)}.");

    public Result<IReadOnlyList<BacklogItem>> ListPending() =>
        Result.Success((IReadOnlyList<BacklogItem>)items.Values
            .Where(i => i.State is TicketState.Unrefined or TicketState.Refined)
            .ToArray());

    public Result<IReadOnlyList<BacklogItem>> ListUnrefined() =>
        throw new NotSupportedException($"{nameof(InMemoryBacklog)} no soporta {nameof(ListUnrefined)}.");

    public Result<IReadOnlyList<BacklogItem>> ListRefined() =>
        throw new NotSupportedException($"{nameof(InMemoryBacklog)} no soporta {nameof(ListRefined)}.");

    public Result<IReadOnlyList<BacklogItem>> ListMissingData() =>
        throw new NotSupportedException($"{nameof(InMemoryBacklog)} no soporta {nameof(ListMissingData)}.");

    public Result<IReadOnlyList<BacklogItem>> ListDone() =>
        throw new NotSupportedException($"{nameof(InMemoryBacklog)} no soporta {nameof(ListDone)}.");

    public Result<IReadOnlyList<BacklogItem>> ListFailed() =>
        throw new NotSupportedException($"{nameof(InMemoryBacklog)} no soporta {nameof(ListFailed)}.");

    public Result<BacklogItem?> GetItem(int number) =>
        GetItemFailure is { } error ? Result.Failure<BacklogItem?>(error) : Result.Success(Get(number));

    public Result Requeue(int number) =>
        throw new NotSupportedException($"{nameof(InMemoryBacklog)} no soporta {nameof(Requeue)}.");

    public Result Close(int number) => ApplyState(number, CloseFailure, TicketState.Done);

    public Result MarkFailed(int number) => ApplyState(number, MarkFailedFailure, TicketState.Failed);

    public Result MarkRefined(int number) => ApplyState(number, MarkRefinedFailure, TicketState.Refined);

    public Result MarkMissingData(int number) => ApplyState(number, MarkMissingDataFailure, TicketState.MissingData);

    public Result Comment(int number, string body)
    {
        if (CommentFailure is { } error) return Result.Failure(error);

        if (items.TryGetValue(number, out var item))
            items[number] = item with { Comments = [.. item.Comments, body] };

        return Result.Success();
    }

    private Result ApplyState(int number, ApplicationError? failure, TicketState state)
    {
        if (failure is { } error) return Result.Failure(error);

        if (items.TryGetValue(number, out var item))
            items[number] = item with { State = state };

        return Result.Success();
    }
}
