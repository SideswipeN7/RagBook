using RagBook.Modules.Folders.Domain;

namespace RagBook.Modules.Folders.Features.ListFolders;

/// <summary>
/// Handles <see cref="ListFoldersQuery"/>. Returns the session's folders (scoped by the persistence
/// layer) as flat <see cref="FolderNode"/>s ordered case-insensitively by name (FR-013); depth is
/// derived from the materialized path.
/// </summary>
public sealed class ListFoldersQueryHandler(IFolderRepository repository)
{
    /// <summary>Returns the ordered folder nodes for the current session.</summary>
    public async Task<IReadOnlyList<FolderNode>> Handle(ListFoldersQuery query, CancellationToken cancellationToken)
    {
        IReadOnlyList<Folder> folders = await repository.ListForSessionAsync(cancellationToken);

        return folders
            .Select(folder => new FolderNode(
                folder.Id,
                folder.ParentId,
                folder.Name,
                FolderPath.Parse(folder.Path).Depth))
            .ToList();
    }
}
