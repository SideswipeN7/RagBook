namespace RagBook.Modules.Chat.Domain;

/// <summary>
/// One passage eligible as grounding (US-13) — everything a future citation (US-16) needs: the source
/// document, its page/location, the passage text, and the relevance distance used for ordering.
/// </summary>
/// <param name="ChunkId">The chunk identity.</param>
/// <param name="DocumentId">The owning document.</param>
/// <param name="FileName">The owning document's file name (for citations).</param>
/// <param name="Text">The passage text.</param>
/// <param name="PageNumber">Source page for PDFs; <c>null</c> for TXT/MD.</param>
/// <param name="Distance">Cosine distance to the query vector — smaller is closer.</param>
public sealed record RetrievedChunk(
    Guid ChunkId,
    Guid DocumentId,
    string FileName,
    string Text,
    int? PageNumber,
    double Distance);
