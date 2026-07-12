namespace RagBook.Modules.Documents.Domain;

/// <summary>A document's status change, pushed to the UI over SSE (US-06).</summary>
/// <param name="DocumentId">The document.</param>
/// <param name="Status">New lifecycle state (<c>Ready</c>/<c>Failed</c>).</param>
/// <param name="ChunkCount">Chunk count when ready; 0 otherwise.</param>
/// <param name="FailureReason">Reason when failed; <c>null</c> otherwise.</param>
public sealed record DocumentStatusUpdate(Guid DocumentId, string Status, int ChunkCount, string? FailureReason);

/// <summary>
/// In-process publish/subscribe for document status changes (US-06), backing the SSE endpoint. The
/// worker publishes a terminal transition for the document's session; the SSE endpoint subscribes to the
/// current session's stream and forwards each update. Per-session, single-instance (documented limit).
/// </summary>
public interface IDocumentStatusNotifier
{
    /// <summary>Publishes <paramref name="update"/> to subscribers of <paramref name="sessionId"/>.</summary>
    void Publish(Guid sessionId, DocumentStatusUpdate update);

    /// <summary>Streams status updates for <paramref name="sessionId"/> until cancelled.</summary>
    IAsyncEnumerable<DocumentStatusUpdate> Subscribe(Guid sessionId, CancellationToken cancellationToken);
}
