namespace RagBook.Modules.Tree.Domain;

/// <summary>The session's folders and documents for one tree read (US-07), each pre-ordered by the reader.</summary>
/// <param name="Folders">Folders, ordered alphabetically (case-insensitive).</param>
/// <param name="Documents">Documents, ordered by upload date descending.</param>
public sealed record TreeData(IReadOnlyList<TreeFolder> Folders, IReadOnlyList<TreeDocument> Documents);

/// <summary>
/// The single read seam for the tree (US-07). One implementation (in Infrastructure) runs two
/// session-scoped queries — folders and documents — so the Tree module never references the Folders or
/// Documents modules directly (constitution §I), and the whole view loads with no per-folder fan-out
/// (FR-001). Reads flow through the global query filter, so only the current session's data is returned.
/// </summary>
public interface ITreeReader
{
    /// <summary>Returns the current session's folders + documents, each already ordered for rendering.</summary>
    Task<TreeData> GetAsync(CancellationToken cancellationToken);
}
