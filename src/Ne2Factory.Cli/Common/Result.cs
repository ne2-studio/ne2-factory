namespace Ne2Factory.Cli.Common;
public readonly record struct Result
{
    private readonly ApplicationError _error;

    private Result(bool isSuccess, ApplicationError error)
    {
        IsSuccess = isSuccess;
        _error = error;
    }

    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public ApplicationError Error =>
        IsFailure ? _error : throw new InvalidOperationException("There is no error for a successful result.");

    public static Result Success() => new(true, default);

    public static Result<T> Success<T>(T value) => Result<T>.Success(value);

    public static Result Failure(ApplicationError error) => new(false, error);

    public static Result<T> Failure<T>(ApplicationError error) => Result<T>.Failure(error);

    // Chains into a value-producing step only on success — the seed for a composition that starts
    // from a non-generic Result (e.g. an authorization check) and continues into a Result<T>-returning
    // step, without a manual IsFailure check in between.
    public Result<T> Bind<T>(Func<Result<T>> next) => IsSuccess ? next() : Result.Failure<T>(_error);

    // Applies `parse` to every item, short-circuiting on the first failure instead of collecting
    // every error — the direct replacement for "foreach item, parse it, return on the first bad
    // one" loops.
    public static Result<List<TOut>> Traverse<TIn, TOut>(IEnumerable<TIn> items, Func<TIn, Result<TOut>> parse)
    {
        var results = new List<TOut>();
        foreach (var item in items)
        {
            var result = parse(item);
            if (result.IsFailure) return Result.Failure<List<TOut>>(result.Error);
            results.Add(result.Value);
        }

        return Result.Success(results);
    }
}

public readonly record struct Result<T>
{
    private readonly T? _value;
    private readonly ApplicationError _error;

    private Result(bool isSuccess, T? value, ApplicationError error)
    {
        IsSuccess = isSuccess;
        _value = value;
        _error = error;
    }

    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public T Value =>
        IsSuccess ? _value! : throw new InvalidOperationException("There is no value for a failed result.");

    public ApplicationError Error =>
        IsFailure ? _error : throw new InvalidOperationException("There is no error for a successful result.");

    public static Result<T> Success(T value) => new(true, value, default);

    public static Result<T> Failure(ApplicationError error) => new(false, default, error);

    public static implicit operator Result<T>(T value) => Success(value);

    // Chains into another Result-returning step only on success, short-circuiting a failure through
    // unchanged — lets independent Result-returning steps compose without a manual IsFailure check
    // after each one.
    public Result<TOut> Bind<TOut>(Func<T, Result<TOut>> next) => IsSuccess ? next(Value) : Result.Failure<TOut>(Error);

    // Transforms a successful value in place; a failure passes through unchanged.
    public Result<TOut> Map<TOut>(Func<T, TOut> map) => IsSuccess ? Result.Success(map(Value)) : Result.Failure<TOut>(Error);
}
