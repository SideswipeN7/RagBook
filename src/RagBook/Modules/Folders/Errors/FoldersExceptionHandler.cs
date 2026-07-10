using RagBook.Shared.Persistence;
using RagBook.Shared.Results;

namespace RagBook.Modules.Folders.Errors;

/// <summary>
/// Translates expected-but-infrastructure-shaped persistence failures into Folders error codes, so the
/// handler contract stays "body or known code" (constitution §II). A unique violation is a duplicate
/// name (the authority for AC-3 under concurrency); a foreign-key violation on delete means the folder
/// still has children (the AC-5 safety net under a concurrent child insert). Genuinely unexpected
/// faults are left unmapped and fall through to the global exception mapper.
/// </summary>
public static class FoldersExceptionHandler
{
    /// <summary>
    /// Maps a classified persistence failure to a Folders error. Returns <c>true</c> and sets
    /// <paramref name="error"/> when the kind is a known, expected failure; otherwise <c>false</c>.
    /// </summary>
    public static bool TryMap(PersistenceErrorKind kind, out Error error)
    {
        switch (kind)
        {
            case PersistenceErrorKind.UniqueViolation:
                error = FolderErrors.DuplicateName;
                return true;
            case PersistenceErrorKind.ForeignKeyViolation:
                error = FolderErrors.NotEmpty;
                return true;
            case PersistenceErrorKind.ConcurrencyConflict:
                error = FolderErrors.Conflict;
                return true;
            case PersistenceErrorKind.Unknown:
            default:
                error = Error.None;
                return false;
        }
    }
}
