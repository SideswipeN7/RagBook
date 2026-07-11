namespace RagBook.Modules.Tree.Domain;

/// <summary>
/// A folder as it appears in the tree read (US-07). Tree-owned (not the Folders module's
/// <c>FolderNode</c>) so the Tree slice needs no reference to the Folders module.
/// </summary>
/// <param name="Id">Folder identity.</param>
/// <param name="ParentId">Parent id, or <c>null</c> at the root.</param>
/// <param name="Name">Display name.</param>
/// <param name="Depth">Nesting depth (root = 1).</param>
public sealed record TreeFolder(Guid Id, Guid? ParentId, string Name, int Depth);
