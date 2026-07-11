namespace RagBook.Modules.Documents.Domain;

/// <summary>What the background worker needs to process a document, read session-agnostically by id.</summary>
/// <param name="SessionId">The owning session (used to bridge the ambient session for the rest of the run).</param>
/// <param name="StoragePath">Where the binary lives (for <c>IFileStorage</c>).</param>
/// <param name="ContentType">Detected content type (selects the extractor).</param>
public sealed record ProcessingTarget(Guid SessionId, string StoragePath, string ContentType);

/// <summary>
/// Reads a document for the background worker (US-06). The worker has no HTTP session, so it first calls
/// <see cref="GetTargetAsync"/> **session-agnostically** (by id, bypassing the query filter) to discover
/// the owning session; after initializing the ambient session to it, it calls
/// <see cref="LoadTrackedAsync"/> for the **session-scoped, tracked** aggregate to apply and persist the
/// status transition. Either returning <c>null</c> means the document was deleted → stop quietly (FR-013).
/// </summary>
public interface IDocumentProcessingReader
{
    /// <summary>Session-agnostic: returns the processing target by id, or <c>null</c> if the document is gone.</summary>
    Task<ProcessingTarget?> GetTargetAsync(Guid documentId, CancellationToken cancellationToken);

    /// <summary>Session-scoped tracked load of the document by id, or <c>null</c> if absent.</summary>
    Task<Document?> LoadTrackedAsync(Guid documentId, CancellationToken cancellationToken);
}
