using RagBook.Modules.Documents.Domain;
using RagBook.Modules.Documents.Errors;
using RagBook.Shared.Results;

namespace RagBook.Modules.Documents.Features.MoveDocument;

/// <summary>
/// Handles <see cref="MoveDocumentCommand"/> (US-10). Validates ownership (a cross-session/unknown document reads
/// as not-found), refuses a read-only demo document, and validates the target folder in the current session via
/// the <see cref="IFolderReference"/> seam. A move to the folder the document is already in is a no-op (no write).
/// The move changes only the owning folder — the indexed content is untouched.
/// </summary>
public sealed class MoveDocumentCommandHandler(IDocumentMoveRepository repository, IFolderReference folders)
{
    /// <summary>Moves the document, or returns a domain error.</summary>
    public async Task<Result> Handle(MoveDocumentCommand command, CancellationToken cancellationToken)
    {
        Document? document = await repository.GetByIdAsync(command.DocumentId, cancellationToken);
        if (document is null)
        {
            return Result.Failure(DocumentErrors.NotFound);
        }

        if (document.Origin == DocumentOrigin.Demo)
        {
            return Result.Failure(DocumentErrors.ReadOnly);
        }

        if (command.TargetFolderId is Guid targetFolderId
            && !await folders.ExistsInSessionAsync(targetFolderId, cancellationToken))
        {
            return Result.Failure(DocumentErrors.TargetFolderNotFound);
        }

        if (document.FolderId == command.TargetFolderId)
        {
            return Result.Success(); // already there — no write
        }

        document.MoveToFolder(command.TargetFolderId);
        await repository.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
