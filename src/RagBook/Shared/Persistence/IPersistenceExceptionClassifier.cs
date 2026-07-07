namespace RagBook.Shared.Persistence;

/// <summary>
/// Classifies a raw persistence exception into a provider-agnostic <see cref="PersistenceErrorKind"/>.
/// Implemented in Infrastructure (which knows the provider); consumed by module exception handlers
/// in Core (which must not).
/// </summary>
public interface IPersistenceExceptionClassifier
{
    /// <summary>Classifies <paramref name="exception"/>; returns <see cref="PersistenceErrorKind.Unknown"/> when unrecognised.</summary>
    PersistenceErrorKind Classify(Exception exception);
}
