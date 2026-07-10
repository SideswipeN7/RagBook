using RagBook.Modules.Folders.Domain;
using RagBook.Modules.Folders.Errors;
using RagBook.Shared.Results;

namespace RagBook.Modules.Folders.Features.DeleteFolder;

/// <summary>
/// Handles <see cref="DeleteFolderCommand"/>. Deletes only an empty folder: it must have no direct
/// children and no files (the file arm via <see cref="IFolderFileProbe"/>, a no-op until US-04). A
/// non-empty folder returns <see cref="FolderErrors.NotEmpty"/> and removes nothing (AC-5). A
/// concurrent child insert is caught by the child foreign key and mapped back to the same code by the
/// repository, so the emptiness guarantee holds under concurrency (FR-009).
/// </summary>
public sealed class DeleteFolderCommandHandler(IFolderRepository repository, IFolderFileProbe fileProbe)
{
    /// <summary>Deletes the folder when empty, or returns a domain error.</summary>
    public async Task<Result> Handle(DeleteFolderCommand command, CancellationToken cancellationToken)
    {
        Folder? folder = await repository.GetByIdAsync(command.Id, cancellationToken);
        if (folder is null)
        {
            return Result.Failure(FolderErrors.NotFound);
        }

        bool hasChildren = await repository.HasChildrenAsync(folder.Id, cancellationToken);
        bool hasFiles = await fileProbe.HasFilesAsync(folder.Id, cancellationToken);
        if (hasChildren || hasFiles)
        {
            return Result.Failure(FolderErrors.NotEmpty);
        }

        repository.Remove(folder);
        await repository.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
