using RagBook.Shared.Messaging;

namespace RagBook.Modules.Folders.Features.MoveFolder;

/// <summary>Moves a folder (with its subtree) under a target folder, or to the root when <paramref name="TargetParentId"/> is null (US-11).</summary>
/// <param name="FolderId">The folder to move.</param>
/// <param name="TargetParentId">The destination parent folder, or <c>null</c> for the root.</param>
public sealed record MoveFolderCommand(Guid FolderId, Guid? TargetParentId) : ICommand;
