using Microsoft.Extensions.Options;
using RagBook.Modules.Folders.Domain;
using RagBook.Modules.Folders.Errors;
using RagBook.Shared.Results;

namespace RagBook.Modules.Folders.Features.CreateFolder;

/// <summary>
/// Handles <see cref="CreateFolderCommand"/>. Name and depth rules live in the <see cref="Folder"/>
/// aggregate; per-parent name uniqueness (AC-3) is enforced by the database and surfaced as
/// <c>folder.duplicate_name</c> by the repository. A parent id owned by another session is invisible
/// to the repository, so it reads as <see cref="FolderErrors.NotFound"/> (→ 404, FR-010).
/// </summary>
public sealed class CreateFolderCommandHandler(IFolderRepository repository, IOptions<FolderOptions> options)
{
    /// <summary>Creates the folder and returns its identity, or a domain error.</summary>
    public async Task<Result<Guid>> Handle(CreateFolderCommand command, CancellationToken cancellationToken)
    {
        FolderOptions folderOptions = options.Value;
        FolderNameRules rules = folderOptions.ToNameRules();

        Result<Folder> creation;
        if (command.ParentId is Guid parentId)
        {
            Folder? parent = await repository.GetByIdAsync(parentId, cancellationToken);
            if (parent is null)
            {
                return Result.Failure<Guid>(FolderErrors.NotFound);
            }

            creation = Folder.CreateChild(parent, command.Name, rules, folderOptions.MaxDepth);
        }
        else
        {
            creation = Folder.CreateRoot(command.Name, rules);
        }

        if (creation.IsFailure)
        {
            return Result.Failure<Guid>(creation.Error);
        }

        Folder folder = creation.Value;
        await repository.AddAsync(folder, cancellationToken);

        return folder.Id;
    }
}
