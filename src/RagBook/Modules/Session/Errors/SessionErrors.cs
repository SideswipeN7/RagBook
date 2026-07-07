using RagBook.Shared.Results;

namespace RagBook.Modules.Session.Errors;

/// <summary>
/// Closed error catalog for the Session module. Handlers may only return codes from this list;
/// codes are stable and namespaced <c>session.*</c> (constitution §II).
/// </summary>
public static class SessionErrors
{
    /// <summary>
    /// Requested resource does not exist for the current session. Returned identically whether the
    /// resource is absent or owned by another session, so existence is never disclosed (AC-3 → 404).
    /// </summary>
    public static readonly Error ResourceNotFound =
        Error.NotFound("session.resource_not_found", "The requested resource does not exist.");

    /// <summary>A resource name was missing or blank.</summary>
    public static readonly Error NameRequired =
        Error.Validation("session.name_required", "Resource name is required.");

    /// <summary>A row collided with an existing one on a unique constraint.</summary>
    public static readonly Error ResourceAlreadyExists =
        Error.Conflict("session.resource_already_exists", "A resource with the same identity already exists.");

    /// <summary>A concurrent update conflicted with the persisted state.</summary>
    public static readonly Error ConcurrencyConflict =
        Error.Conflict("session.concurrency_conflict", "The resource was modified concurrently.");
}
