namespace RagBook.Modules.Documents.Domain;

/// <summary>
/// Persistence seam for moving a document between folders (US-10). Reads flow through the session query filter,
/// so a document owned by another session reads as <c>null</c> (→ 404). Returns a tracked aggregate so the handler
/// can call <see cref="Document.MoveToFolder"/> and persist it with <see cref="SaveChangesAsync"/>.
/// </summary>
public interface IDocumentMoveRepository
{
    /// <summary>Returns the tracked document by id, or <c>null</c> when absent or owned by another session.</summary>
    Task<Document?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Flushes the pending folder change.</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
