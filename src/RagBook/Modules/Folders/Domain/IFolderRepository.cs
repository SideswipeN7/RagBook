namespace RagBook.Modules.Folders.Domain;

/// <summary>
/// Persistence seam for <see cref="Folder"/>. Every read is automatically constrained to the current
/// session by the EF Core global query filter behind the implementation (constitution §III) — this
/// interface exposes no way to query across sessions, so a cross-session id reads as absent (→ 404).
/// Writes translate expected persistence faults (duplicate name, child FK) into Folders error codes.
/// </summary>
public interface IFolderRepository
{
    /// <summary>Persists a new folder; the owning session is stamped centrally on save.</summary>
    Task AddAsync(Folder folder, CancellationToken cancellationToken);

    /// <summary>Returns the tracked folder by id, or <c>null</c> when absent or owned by another session.</summary>
    Task<Folder?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>True when the folder has at least one direct child in the current session (AC-5).</summary>
    Task<bool> HasChildrenAsync(Guid folderId, CancellationToken cancellationToken);

    /// <summary>Returns the current session's folders, ordered case-insensitively by name (FR-013).</summary>
    Task<IReadOnlyList<Folder>> ListForSessionAsync(CancellationToken cancellationToken);

    /// <summary>Marks a tracked folder for deletion; persisted by <see cref="SaveChangesAsync"/>.</summary>
    void Remove(Folder folder);

    /// <summary>Flushes pending changes, translating expected persistence faults to Folders codes.</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
