namespace RagBook.Shared.Results;

/// <summary>
/// Outcome of an operation: either success, or a single <see cref="Error"/>. Handlers return a
/// result for every expected outcome and never throw for domain failures (constitution §II/§IV).
/// </summary>
public class Result
{
    /// <summary>Creates a result in the given state.</summary>
    protected Result(bool isSuccess, Error error)
    {
        if (isSuccess && error != Error.None)
        {
            throw new InvalidOperationException("A successful result cannot carry an error.");
        }

        if (!isSuccess && error == Error.None)
        {
            throw new InvalidOperationException("A failed result must carry an error.");
        }

        IsSuccess = isSuccess;
        Error = error;
    }

    /// <summary>True when the operation succeeded.</summary>
    public bool IsSuccess { get; }

    /// <summary>True when the operation failed.</summary>
    public bool IsFailure => !IsSuccess;

    /// <summary>The error for a failed result, or <see cref="Error.None"/> for a success.</summary>
    public Error Error { get; }

    /// <summary>Creates a successful result.</summary>
    public static Result Success()
    {
        return new Result(true, Error.None);
    }

    /// <summary>Creates a failed result carrying <paramref name="error"/>.</summary>
    public static Result Failure(Error error)
    {
        return new Result(false, error);
    }

    /// <summary>Creates a successful result carrying <paramref name="value"/>.</summary>
    public static Result<TValue> Success<TValue>(TValue value)
    {
        return Result<TValue>.Success(value);
    }

    /// <summary>Creates a failed <see cref="Result{TValue}"/> carrying <paramref name="error"/>.</summary>
    public static Result<TValue> Failure<TValue>(Error error)
    {
        return Result<TValue>.Failure(error);
    }
}
