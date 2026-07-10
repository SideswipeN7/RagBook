using RagBook.Shared.Messaging;

namespace RagBook.Modules.Folders.Features.ListFolders;

/// <summary>Lists the current session's folders, ordered for tree composition (US-09, FR-013).</summary>
public sealed record ListFoldersQuery : IQuery<IReadOnlyList<FolderNode>>;
