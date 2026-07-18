using RagBook.Modules.Tree.Domain;

namespace RagBook.Modules.Tree.Features.GetTree;

/// <summary>
/// The single response of <c>GET /api/tree</c> (US-07): the session's folders and documents together,
/// each pre-ordered (folders A→Z, documents newest-first). The client composes the nested tree.
/// </summary>
/// <param name="Folders">Folders ordered alphabetically (case-insensitive).</param>
/// <param name="Documents">The session's own documents ordered by upload date descending.</param>
/// <param name="Demo">The global read-only demo documents (US-03), ordered newest-first.</param>
public sealed record TreeResponse(
    IReadOnlyList<TreeFolder> Folders,
    IReadOnlyList<TreeDocument> Documents,
    IReadOnlyList<TreeDocument> Demo);
