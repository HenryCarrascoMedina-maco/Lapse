using System.Diagnostics.CodeAnalysis;

namespace Lapse.Core;

public readonly record struct Result<T>
{
    private readonly T? value;

    private Result(T? value, string? error)
    {
        this.value = value;
        Error = error;
    }

    public string? Error { get; }

    [MemberNotNullWhen(false, nameof(Error))]
    public bool IsSuccess => Error is null;

    public T Value => IsSuccess ? value! : throw new InvalidOperationException($"The result is a failure: {Error}");

    public Result<TOut> Map<TOut>(Func<T, TOut> map) =>
        IsSuccess ? Result.Success(map(Value)) : Result.Failure<TOut>(Error);

    public Result<TOut> Bind<TOut>(Func<T, Result<TOut>> bind) =>
        IsSuccess ? bind(Value) : Result.Failure<TOut>(Error);

    internal static Result<T> FromValue(T value) => new(value, null);

    internal static Result<T> FromError(string error) => new(default, error);
}

public static class Result
{
    public static Result<T> Success<T>(T value) => Result<T>.FromValue(value);

    public static Result<T> Failure<T>(string error) => Result<T>.FromError(error);
}

public readonly record struct Unit
{
    public static Unit Value => default;
}
