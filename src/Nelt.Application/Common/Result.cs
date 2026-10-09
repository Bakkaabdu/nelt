namespace Nelt.Application.Common;

public enum ErrorKind
{
    Validation = 1,
    NotFound = 2,
    Forbidden = 3,
    Conflict = 4,
}

/// <summary>An expected failure. <see cref="Message"/> is English text that doubles as the localization key.</summary>
public sealed record Error(ErrorKind Kind, string Message, string? Field = null)
{
    public static Error NotFound(string message = "The requested item was not found.") => new(ErrorKind.NotFound, message);
    public static Error Forbidden(string message = "You do not have access to this item.") => new(ErrorKind.Forbidden, message);
    public static Error Validation(string message, string? field = null) => new(ErrorKind.Validation, message, field);
    public static Error Conflict(string message) => new(ErrorKind.Conflict, message);
}

public class Result
{
    protected Result(Error? error) => Error = error;

    public Error? Error { get; }
    public bool Succeeded => Error is null;
    public bool Failed => Error is not null;

    public static Result Success() => new(null);
    public static Result Fail(Error error) => new(error);
    public static Result<T> Success<T>(T value) => Result<T>.Ok(value);

    public static implicit operator Result(Error error) => new(error);
}

public sealed class Result<T> : Result
{
    private readonly T? _value;

    private Result(T? value, Error? error) : base(error) => _value = value;

    public T Value => Succeeded ? _value! : throw new InvalidOperationException("A failed result has no value.");

    public static Result<T> Ok(T value) => new(value, null);
    public static new Result<T> Fail(Error error) => new(default, error);

    public static implicit operator Result<T>(T value) => Ok(value);
    public static implicit operator Result<T>(Error error) => Fail(error);
}
