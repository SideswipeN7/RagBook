namespace RagBook.Modules.Folders.Features.ListFolders;

/// <summary>
/// A flat projection of a folder for the tree read (US-09). The client composes the tree from
/// <see cref="ParentId"/>; <see cref="Depth"/> lets the UI hide "New folder" at the maximum depth.
/// </summary>
/// <param name="Id">Folder identity.</param>
/// <param name="ParentId">Parent id, or <c>null</c> at the root.</param>
/// <param name="Name">Display name.</param>
/// <param name="Depth">Nesting depth (root = 1).</param>
public sealed record FolderNode(Guid Id, Guid? ParentId, string Name, int Depth);
