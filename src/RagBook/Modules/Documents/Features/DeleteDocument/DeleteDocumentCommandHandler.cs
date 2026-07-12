using RagBook.Modules.Documents.Domain;
using RagBook.Modules.Documents.Errors;
using RagBook.Shared.Results;

namespace RagBook.Modules.Documents.Features.DeleteDocument;

/// <summary>
/// Handles <see cref="DeleteDocumentCommand"/> (US-08). Delegates the transactional delete + cascade +
/// best-effort blob cleanup to the repository; a document invisible to the session (cross-session,
/// already-deleted, or unknown) reads as not-found → <c>document.not_found</c> (→ 404), which is also the
/// idempotent result of a repeat delete.
/// </summary>
public sealed class DeleteDocumentCommandHandler(IDocumentDeletionRepository repository)
{
    /// <summary>Deletes the document, or returns not-found.</summary>
    public async Task<Result> Handle(DeleteDocumentCommand command, CancellationToken cancellationToken)
    {
        bool deleted = await repository.DeleteAsync(command.Id, cancellationToken);

        return deleted ? Result.Success() : Result.Failure(DocumentErrors.NotFound);
    }
}
