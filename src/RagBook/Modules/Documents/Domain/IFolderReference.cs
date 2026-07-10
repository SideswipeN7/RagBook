namespace RagBook.Modules.Documents.Domain;

/// <summary>
/// A session-scoped existence check for an upload's target folder, owned by the Documents module so it
/// never references the Folders module's types directly (constitution §I). The implementation lives in
/// Infrastructure (which may see both modules) and reads through the session query filter, so a folder
/// owned by another session reads as absent (→ 404, FR-006).
/// </summary>
public interface IFolderReference
{
    /// <summary>True when <paramref name="folderId"/> is a folder in the current session.</summary>
    Task<bool> ExistsInSessionAsync(Guid folderId, CancellationToken cancellationToken);
}
