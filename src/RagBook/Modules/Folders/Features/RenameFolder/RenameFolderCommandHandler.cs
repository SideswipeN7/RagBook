using Microsoft.Extensions.Options;
using RagBook.Modules.Folders.Domain;
using RagBook.Modules.Folders.Errors;
using RagBook.Shared.Results;

namespace RagBook.Modules.Folders.Features.RenameFolder;

/// <summary>
/// Handles <see cref="RenameFolderCommand"/>. Loads the tracked folder (invisible across sessions →
/// <see cref="FolderErrors.NotFound"/>), applies the name change via the aggregate (path and
/// descendants untouched — AC-4), and saves; a sibling-name clash surfaces as
/// <c>folder.duplicate_name</c> from the database (AC-3).
/// </summary>
public sealed class RenameFolderCommandHandler(IFolderRepository repository, IOptions<FolderOptions> options)
{
    /// <summary>Renames the folder, or returns a domain error.</summary>
    public async Task<Result> Handle(RenameFolderCommand command, CancellationToken cancellationToken)
    {
        Folder? folder = await repository.GetByIdAsync(command.Id, cancellationToken);
        if (folder is null)
        {
            return Result.Failure(FolderErrors.NotFound);
        }

        Result rename = folder.Rename(command.NewName, options.Value.ToNameRules());
        if (rename.IsFailure)
        {
            return rename;
        }

        await repository.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
