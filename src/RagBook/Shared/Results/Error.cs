namespace RagBook.Shared.Results;

/// <summary>
/// A single, machine-readable failure. <see cref="Code"/> is stable and namespaced per module
/// (e.g. <c>session.resource_not_found</c>); the frontend branches on the code, never the message.
/// </summary>
/// <param name="Code">Stable, machine-readable, module-namespaced code.</param>
/// <param name="Message">Human-readable description (never the contract the client branches on).</param>
/// <param name="Type">Category driving HTTP status mapping.</param>
public sealed record Error(string Code, string Message, ErrorType Type)
{
    /// <summary>The absence of an error, used by successful results.</summary>
    public static readonly Error None = new(string.Empty, string.Empty, ErrorType.Failure);

    /// <summary>Creates a <see cref="ErrorType.Validation"/> error.</summary>
    public static Error Validation(string code, string message)
    {
        return new Error(code, message, ErrorType.Validation);
    }

    /// <summary>Creates a <see cref="ErrorType.NotFound"/> error.</summary>
    public static Error NotFound(string code, string message)
    {
        return new Error(code, message, ErrorType.NotFound);
    }

    /// <summary>Creates a <see cref="ErrorType.Conflict"/> error.</summary>
    public static Error Conflict(string code, string message)
    {
        return new Error(code, message, ErrorType.Conflict);
    }

    /// <summary>Creates an <see cref="ErrorType.Unexpected"/> error.</summary>
    public static Error Unexpected(string code, string message)
    {
        return new Error(code, message, ErrorType.Unexpected);
    }
}
