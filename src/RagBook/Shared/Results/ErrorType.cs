namespace RagBook.Shared.Results;

/// <summary>
/// Category of an <see cref="Error"/>. The web layer maps each category to an HTTP status
/// (RFC 9457 ProblemDetails) while always surfacing the stable <see cref="Error.Code"/>.
/// </summary>
public enum ErrorType
{
    /// <summary>Generic expected failure with no more specific category.</summary>
    Failure = 0,

    /// <summary>Input failed validation. Maps to HTTP 400.</summary>
    Validation = 1,

    /// <summary>Requested resource does not exist (or is not visible to the caller). Maps to HTTP 404.</summary>
    NotFound = 2,

    /// <summary>Request conflicts with current state (e.g. uniqueness). Maps to HTTP 409.</summary>
    Conflict = 3,

    /// <summary>Caller is not authenticated. Maps to HTTP 401.</summary>
    Unauthorized = 4,

    /// <summary>Caller lacks the required permission. Maps to HTTP 403.</summary>
    Forbidden = 5,

    /// <summary>Unexpected fault. Maps to a sanitized HTTP 500.</summary>
    Unexpected = 6,
}
