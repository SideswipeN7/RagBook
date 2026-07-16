using Microsoft.Extensions.Options;
using RagBook.Modules.Documents.Domain;
using RagBook.Modules.Documents.Features.Bulk;
using RagBook.Shared.Results;

namespace RagBook.Modules.Documents.Features.BulkDelete;

/// <summary>
/// Handles <see cref="BulkDeleteCommand"/> (US-12). De-duplicates and size-guards the id list (empty / over-cap →
/// 400), then validates <b>every</b> id against the session before any write: a missing id reads as
/// <c>document.not_found</c> (no existence disclosure) and a demo document as <c>document.read_only</c>. If any
/// item fails, the whole operation is refused with the per-id list and nothing is deleted; otherwise all documents
/// (and their chunks, by cascade) are deleted in one transaction and the quota drops by the number deleted.
/// </summary>
public sealed class BulkDeleteCommandHandler(
    IDocumentBulkRepository repository,
    IOptions<BulkOptions> options)
{
    /// <summary>Deletes every selected document, or returns the all-or-nothing failure.</summary>
    public async Task<BulkResult> Handle(BulkDeleteCommand command, CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<Guid>> normalized = BulkValidation.Normalize(command.Ids, options.Value.MaxItems);
        if (normalized.IsFailure)
        {
            return BulkResult.BadRequest(normalized.Error);
        }

        IReadOnlyList<Guid> ids = normalized.Value;
        IReadOnlyList<Document> documents = await repository.GetByIdsAsync(ids, cancellationToken);

        List<BulkFailure> failures = BulkValidation.FindItemFailures(ids, documents);
        if (failures.Count > 0)
        {
            return BulkResult.ValidationFailed(failures);
        }

        await repository.DeleteAllAsync(documents, cancellationToken);
        return BulkResult.Success();
    }
}
