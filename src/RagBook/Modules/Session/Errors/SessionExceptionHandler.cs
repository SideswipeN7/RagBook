using RagBook.Shared.Persistence;
using RagBook.Shared.Results;

namespace RagBook.Modules.Session.Errors;

/// <summary>
/// Translates expected-but-infrastructure-shaped persistence failures (surfaced at the DB boundary)
/// into the Session module's domain error codes, so the handler contract stays "body or known code"
/// (constitution §II). Genuinely unexpected faults are left unmapped and fall through to the global
/// exception mapper.
/// </summary>
public static class SessionExceptionHandler
{
    /// <summary>
    /// Maps a classified persistence failure to a Session error. Returns <c>true</c> and sets
    /// <paramref name="error"/> when the kind is a known, expected failure; otherwise <c>false</c>.
    /// </summary>
    public static bool TryMap(PersistenceErrorKind kind, out Error error)
    {
        switch (kind)
        {
            case PersistenceErrorKind.UniqueViolation:
                error = SessionErrors.ResourceAlreadyExists;
                return true;
            case PersistenceErrorKind.ConcurrencyConflict:
                error = SessionErrors.ConcurrencyConflict;
                return true;
            case PersistenceErrorKind.ForeignKeyViolation:
            case PersistenceErrorKind.Unknown:
            default:
                error = Error.None;
                return false;
        }
    }
}
