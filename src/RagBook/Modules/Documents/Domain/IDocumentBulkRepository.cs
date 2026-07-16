namespace RagBook.Modules.Documents.Domain;

/// <summary>
/// Persistence seam for the all-or-nothing bulk operations (US-12). Reads flow through the session query filter,
/// so a document owned by another session is simply absent from <see cref="GetByIdsAsync"/> — reported as
/// not-found without disclosing its existence (constitution §III). The handler validates the whole set against
/// the returned documents before calling <see cref="MoveAllAsync"/> or <see cref="DeleteAllAsync"/>, each of
/// which applies the change to every document in a single transaction (no partial apply).
/// </summary>
public interface IDocumentBulkRepository
{
    /// <summary>
    /// Returns the current session's documents whose id is in <paramref name="ids"/> (tracked), in any order.
    /// Ids not in the session (cross-session / unknown / already deleted) are simply omitted.
    /// </summary>
    Task<IReadOnlyList<Document>> GetByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    /// <summary>
    /// Moves every document in <paramref name="documents"/> to <paramref name="targetFolderId"/> (or the root when
    /// <c>null</c>) in one <c>SaveChanges</c>. Only the owning folder changes — the indexed content is untouched.
    /// </summary>
    Task MoveAllAsync(IReadOnlyList<Document> documents, Guid? targetFolderId, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes every document in <paramref name="documents"/> in one transaction — the <c>chunks</c> FK cascades
    /// the index away (US-06) — then makes a best-effort removal of each stored binary (a storage failure is
    /// logged, not thrown; an orphaned blob is the accepted trade-off, reusing the US-08 pattern).
    /// </summary>
    Task DeleteAllAsync(IReadOnlyList<Document> documents, CancellationToken cancellationToken);
}
