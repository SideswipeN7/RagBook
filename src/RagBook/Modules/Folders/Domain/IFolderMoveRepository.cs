namespace RagBook.Modules.Folders.Domain;

/// <summary>
/// Persistence seam for moving a folder with its subtree (US-11). Reads flow through the session query filter
/// (a cross-session id reads as <c>null</c> → 404). <see cref="MoveAsync"/> re-parents the folder and rewrites the
/// materialized-path prefix of the folder and every descendant in one transaction, explicitly scoped to the
/// current session (the global filter does not apply to raw bulk SQL — constitution §III).
/// </summary>
public interface IFolderMoveRepository
{
    /// <summary>Returns the tracked folder by id, or <c>null</c> when absent or owned by another session.</summary>
    Task<Folder?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>The maximum nesting depth among the folder identified by <paramref name="pathPrefix"/> and its descendants.</summary>
    Task<int> MaxSubtreeDepthAsync(string pathPrefix, CancellationToken cancellationToken);

    /// <summary>True when a folder (other than <paramref name="excludeId"/>) with <paramref name="name"/> already exists directly under <paramref name="parentId"/> (case-insensitive; <c>null</c> = root).</summary>
    Task<bool> SiblingExistsAsync(Guid? parentId, string name, Guid excludeId, CancellationToken cancellationToken);

    /// <summary>Atomically re-parents the folder and rewrites the path prefix of it and every descendant (session-scoped).</summary>
    Task MoveAsync(Guid movedId, Guid? newParentId, string oldPrefix, string newPrefix, CancellationToken cancellationToken);
}
