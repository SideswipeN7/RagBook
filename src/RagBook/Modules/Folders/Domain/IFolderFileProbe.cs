namespace RagBook.Modules.Folders.Domain;

/// <summary>
/// The "does this folder contain files?" arm of the delete-emptiness rule (AC-5). Documents gain a
/// <c>folder_id</c> only in US-04, so US-09 ships a no-op implementation that always reports "no
/// files"; US-04 replaces it with a real query. This is the single integration point US-04 touches to
/// complete AC-5 end-to-end — the delete handler stays unchanged.
/// </summary>
public interface IFolderFileProbe
{
    /// <summary>True when the folder holds at least one file in the current session.</summary>
    Task<bool> HasFilesAsync(Guid folderId, CancellationToken cancellationToken);
}
