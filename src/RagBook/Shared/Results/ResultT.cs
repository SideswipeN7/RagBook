namespace RagBook.Shared.Results;

/// <summary>
/// Outcome of an operation that yields a value on success. Access <see cref="Value"/> only when
/// <see cref="Result.IsSuccess"/> is true.
/// </summary>
/// <typeparam name="TValue">The success payload type.</typeparam>
public sealed class Result<TValue> : Result
{
    private readonly TValue? _value;

    private Result(TValue? value, bool isSuccess, Error error)
        : base(isSuccess, error)
    {
        _value = value;
    }

    /// <summary>The success payload. Throws when the result is a failure.</summary>
    public TValue Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("Cannot access the value of a failed result.");

    /// <summary>Creates a successful result carrying <paramref name="value"/>.</summary>
    public static Result<TValue> Success(TValue value)
    {
        return new Result<TValue>(value, true, Error.None);
    }

    /// <summary>Creates a failed result carrying <paramref name="error"/>.</summary>
    public static new Result<TValue> Failure(Error error)
    {
        return new Result<TValue>(default, false, error);
    }

    /// <summary>Lifts a value into a successful result.</summary>
    public static implicit operator Result<TValue>(TValue value)
    {
        return Success(value);
    }

    /// <summary>Lifts an error into a failed result.</summary>
    public static implicit operator Result<TValue>(Error error)
    {
        return Failure(error);
    }
}
