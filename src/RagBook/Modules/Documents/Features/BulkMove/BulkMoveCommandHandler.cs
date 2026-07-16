using Microsoft.Extensions.Options;
using RagBook.Modules.Documents.Domain;
using RagBook.Modules.Documents.Errors;
using RagBook.Modules.Documents.Features.Bulk;
using RagBook.Shared.Results;

namespace RagBook.Modules.Documents.Features.BulkMove;

/// <summary>
/// Handles <see cref="BulkMoveCommand"/> (US-12). De-duplicates and size-guards the id list (empty / over-cap →
/// 400), then validates <b>every</b> id against the session before any write: a missing id reads as
/// <c>document.not_found</c> (no existence disclosure), a demo document as <c>document.read_only</c>, and — when a
/// target folder is given but absent from the session — one <c>folder.not_found</c> failure keyed by the target
/// folder id. If any item fails, the whole operation is refused with the per-id list and nothing moves; otherwise
/// all documents are moved in one <c>SaveChanges</c>.
/// </summary>
public sealed class BulkMoveCommandHandler(
    IDocumentBulkRepository repository,
    IFolderReference folders,
    IOptions<BulkOptions> options)
{
    /// <summary>Moves every selected document, or returns the all-or-nothing failure.</summary>
    public async Task<BulkResult> Handle(BulkMoveCommand command, CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<Guid>> normalized = BulkValidation.Normalize(command.Ids, options.Value.MaxItems);
        if (normalized.IsFailure)
        {
            return BulkResult.BadRequest(normalized.Error);
        }

        IReadOnlyList<Guid> ids = normalized.Value;
        IReadOnlyList<Document> documents = await repository.GetByIdsAsync(ids, cancellationToken);

        List<BulkFailure> failures = BulkValidation.FindItemFailures(ids, documents);

        if (command.TargetFolderId is Guid targetFolderId
            && !await folders.ExistsInSessionAsync(targetFolderId, cancellationToken))
        {
            failures.Add(new BulkFailure(targetFolderId, DocumentErrors.TargetFolderNotFound.Code));
        }

        if (failures.Count > 0)
        {
            return BulkResult.ValidationFailed(failures);
        }

        await repository.MoveAllAsync(documents, command.TargetFolderId, cancellationToken);
        return BulkResult.Success();
    }
}
