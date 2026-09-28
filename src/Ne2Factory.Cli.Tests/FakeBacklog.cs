using Ne2Factory.Cli.Backlog;
using Ne2Factory.Cli.Common;

namespace Ne2Factory.Cli.Tests;

internal sealed class FakeBacklog : IBacklog
{
    public Result<BacklogItem?> GetItemResult { get; set; } = Result.Success<BacklogItem?>(null);
    public Result CommentResult { get; set; } = Result.Success();
    public Result CloseResult { get; set; } = Result.Success();
    public Result MarkFailedResult { get; set; } = Result.Success();
    public Result MarkRefinedResult { get; set; } = Result.Success();
    public Result MarkMissingDataResult { get; set; } = Result.Success();

    public List<int> GetItemCalls { get; } = [];
    public List<(int Number, string Body)> Comments { get; } = [];
    public List<int> ClosedNumbers { get; } = [];
    public List<int> MarkFailedNumbers { get; } = [];
    public List<int> MarkRefinedNumbers { get; } = [];
    public List<int> MarkMissingDataNumbers { get; } = [];

    public void EnsureLabels() =>
        throw new NotSupportedException($"{nameof(FakeBacklog)} no soporta {nameof(EnsureLabels)}.");

    public Result<IReadOnlyList<BacklogItem>> ListUnrefined() =>
        throw new NotSupportedException($"{nameof(FakeBacklog)} no soporta {nameof(ListUnrefined)}.");

    public Result<IReadOnlyList<BacklogItem>> ListRefined() =>
        throw new NotSupportedException($"{nameof(FakeBacklog)} no soporta {nameof(ListRefined)}.");

    public Result<IReadOnlyList<BacklogItem>> ListMissingData() =>
        throw new NotSupportedException($"{nameof(FakeBacklog)} no soporta {nameof(ListMissingData)}.");

    public Result<IReadOnlyList<BacklogItem>> ListDone() =>
        throw new NotSupportedException($"{nameof(FakeBacklog)} no soporta {nameof(ListDone)}.");

    public Result<IReadOnlyList<BacklogItem>> ListFailed() =>
        throw new NotSupportedException($"{nameof(FakeBacklog)} no soporta {nameof(ListFailed)}.");

    public Result<BacklogItem?> GetItem(int number)
    {
        GetItemCalls.Add(number);
        return GetItemResult;
    }

    public Result Requeue(int number) =>
        throw new NotSupportedException($"{nameof(FakeBacklog)} no soporta {nameof(Requeue)}.");

    public Result Close(int number)
    {
        ClosedNumbers.Add(number);
        return CloseResult;
    }

    public Result MarkFailed(int number)
    {
        MarkFailedNumbers.Add(number);
        return MarkFailedResult;
    }

    public Result MarkRefined(int number)
    {
        MarkRefinedNumbers.Add(number);
        return MarkRefinedResult;
    }

    public Result MarkMissingData(int number)
    {
        MarkMissingDataNumbers.Add(number);
        return MarkMissingDataResult;
    }

    public Result Comment(int number, string body)
    {
        Comments.Add((number, body));
        return CommentResult;
    }
}
