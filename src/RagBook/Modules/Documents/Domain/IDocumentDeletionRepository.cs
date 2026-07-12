namespace RagBook.Modules.Documents.Domain;

/// <summary>
/// Persistence seam for hard-deleting a document (US-08). Session-scoped: a document owned by another
/// session (or already deleted / unknown) is invisible, so it reads as not-found. The implementation
/// deletes the row in a transaction — the <c>chunks</c> foreign key removes the index by cascade (US-06)
/// — and then makes a **best-effort** removal of the stored binary (a storage failure is logged, not
/// thrown; an orphaned blob is the accepted trade-off, FR-004).
/// </summary>
public interface IDocumentDeletionRepository
{
    /// <summary>Deletes the current session's document by id; returns <c>false</c> when it does not exist.</summary>
    Task<bool> DeleteAsync(Guid documentId, CancellationToken cancellationToken);
}
