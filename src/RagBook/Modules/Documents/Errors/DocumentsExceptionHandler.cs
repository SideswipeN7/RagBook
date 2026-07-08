using RagBook.Shared.Persistence;
using RagBook.Shared.Results;

namespace RagBook.Modules.Documents.Errors;

/// <summary>
/// Translates expected-but-infrastructure-shaped persistence failures (surfaced at the DB boundary)
/// into the Documents module's domain error codes, so the handler contract stays "body or known code"
/// (constitution §II). With the advisory-lock admit, over-quota is returned as a <see cref="Result"/>
/// rather than thrown, so this handler is a safety net for races, not the primary path. Genuinely
/// unexpected faults are left unmapped and fall through to the global exception mapper.
/// </summary>
public static class DocumentsExceptionHandler
{
    /// <summary>
    /// Maps a classified persistence failure to a Documents error. Returns <c>true</c> and sets
    /// <paramref name="error"/> when the kind is a known, expected failure; otherwise <c>false</c>.
    /// </summary>
    public static bool TryMap(PersistenceErrorKind kind, out Error error)
    {
        switch (kind)
        {
            case PersistenceErrorKind.UniqueViolation:
            case PersistenceErrorKind.ConcurrencyConflict:
                error = QuotaErrors.Conflict;
                return true;
            case PersistenceErrorKind.ForeignKeyViolation:
            case PersistenceErrorKind.Unknown:
            default:
                error = Error.None;
                return false;
        }
    }
}
