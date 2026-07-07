namespace RagBook.Shared.Results;

/// <summary>
/// Carries a domain <see cref="Error"/> across a boundary where a <see cref="Result"/> cannot be
/// returned (e.g. deep in the persistence layer). The global exception mapper converts it into an
/// RFC 9457 ProblemDetails with the error <see cref="Error.Code"/> — never a naked 500.
/// </summary>
public sealed class DomainException(Error error) : Exception(error.Message)
{
    /// <summary>The domain error this exception represents.</summary>
    public Error Error { get; } = error;
}
