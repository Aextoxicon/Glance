using System;

namespace Glance.Utils;

public abstract record Result<T>
{
    public bool IsSuccess => this is Success<T>;
    public bool IsFailure => this is Failure<T>;

    public R Match<R>(Func<T, R> onSuccess, Func<Exception, R> onFailure) => this switch
    {
        Success<T> s => onSuccess(s.Value),
        Failure<T> f => onFailure(f.Error),
        _ => throw new InvalidOperationException("未识别的 Result 分支"),
    };

    public T? GetOrNull() => this is Success<T> s ? s.Value : default;

    public static Result<T> Success(T value) => new Success<T>(value);
    public static Result<T> Failure(Exception error) => new Failure<T>(error);
}

public sealed record Success<T>(T Value) : Result<T>;

public sealed record Failure<T>(Exception Error) : Result<T>;
