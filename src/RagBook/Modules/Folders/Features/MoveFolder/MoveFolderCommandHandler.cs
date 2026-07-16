using Microsoft.Extensions.Options;
using RagBook.Modules.Folders.Domain;
using RagBook.Modules.Folders.Errors;
using RagBook.Shared.Results;

namespace RagBook.Modules.Folders.Features.MoveFolder;

/// <summary>
/// Handles <see cref="MoveFolderCommand"/> (US-11). Validates ownership, cycle (materialized-path prefix — catches
/// self-move and descendant-move), resulting depth, and a same-name sibling in the target; then re-parents the
/// folder and rewrites its subtree's path prefix atomically. A move to the current parent is a no-op. Documents
/// are untouched — they follow their folder.
/// </summary>
public sealed class MoveFolderCommandHandler(IFolderMoveRepository repository, IOptions<FolderOptions> options)
{
    /// <summary>Moves the folder, or returns a domain error.</summary>
    public async Task<Result> Handle(MoveFolderCommand command, CancellationToken cancellationToken)
    {
        Folder? moved = await repository.GetByIdAsync(command.FolderId, cancellationToken);
        if (moved is null)
        {
            return Result.Failure(FolderErrors.NotFound);
        }

        if (moved.ParentId == command.TargetParentId)
        {
            return Result.Success(); // already under that parent — no write
        }

        FolderPath movedPath = FolderPath.Parse(moved.Path);
        FolderPath newPath;

        if (command.TargetParentId is Guid targetId)
        {
            Folder? target = await repository.GetByIdAsync(targetId, cancellationToken);
            if (target is null)
            {
                return Result.Failure(FolderErrors.NotFound);
            }

            FolderPath targetPath = FolderPath.Parse(target.Path);
            if (movedPath.IsPrefixOf(targetPath))
            {
                return Result.Failure(FolderErrors.CircularMove);
            }

            newPath = targetPath.Append(moved.Id);
        }
        else
        {
            newPath = FolderPath.ForRoot(moved.Id);
        }

        // Resulting depth = the moved folder's new depth + how much deeper its deepest descendant sits.
        int maxDescendantDepth = await repository.MaxSubtreeDepthAsync(movedPath.Value, cancellationToken);
        int subtreeHeight = maxDescendantDepth - movedPath.Depth;
        if (newPath.Depth + subtreeHeight > options.Value.MaxDepth)
        {
            return Result.Failure(FolderErrors.MaxDepthExceeded);
        }

        if (await repository.SiblingExistsAsync(command.TargetParentId, moved.Name, moved.Id, cancellationToken))
        {
            return Result.Failure(FolderErrors.DuplicateName);
        }

        await repository.MoveAsync(moved.Id, command.TargetParentId, movedPath.Value, newPath.Value, cancellationToken);

        return Result.Success();
    }
}
