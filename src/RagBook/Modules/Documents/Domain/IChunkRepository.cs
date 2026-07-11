namespace RagBook.Modules.Documents.Domain;

/// <summary>
/// Persistence seam for a document's chunks (US-06). Both operations take the **tracked**
/// <see cref="Document"/> (already <see cref="Document.MarkReady"/>/<see cref="Document.MarkFailed"/>-ed
/// by the handler) and persist the chunk write **and** the status transition in **one transaction**:
/// <see cref="ReplaceForDocumentAsync"/> deletes the document's existing chunks then inserts the new set
/// (idempotent — a re-run yields the same set); <see cref="DeleteForDocumentAsync"/> removes any partial
/// chunks on failure (no partial index). Chunk cleanup on document delete is handled by the DB cascade.
/// </summary>
public interface IChunkRepository
{
    /// <summary>Replaces the document's chunks with <paramref name="chunks"/> and saves the document + chunks atomically.</summary>
    Task ReplaceForDocumentAsync(Document document, IReadOnlyList<Chunk> chunks, CancellationToken cancellationToken);

    /// <summary>Deletes the document's chunks and saves the document (failed) — leaves no partial index.</summary>
    Task DeleteForDocumentAsync(Document document, CancellationToken cancellationToken);
}
