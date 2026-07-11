namespace RagBook.Modules.Tree.Domain;

/// <summary>
/// A document as it appears in the tree read (US-07): display metadata projected from the US-04
/// document. Tree-owned so the Tree slice needs no reference to the Documents module.
/// </summary>
/// <param name="Id">Document identity.</param>
/// <param name="FolderId">Owning folder, or <c>null</c> for a root document.</param>
/// <param name="FileName">Display name incl. extension.</param>
/// <param name="ContentType">Detected content type.</param>
/// <param name="SizeBytes">Size in bytes (formatted for display on the client).</param>
/// <param name="Status">Lifecycle state — <c>Processing</c>/<c>Ready</c>/<c>Failed</c>.</param>
/// <param name="ChunkCount">Chunks produced by processing (US-06); 0 until then.</param>
/// <param name="UploadedAt">When the upload was recorded.</param>
/// <param name="FailureReason">Reason for a failed document, or <c>null</c> (populated by US-06).</param>
public sealed record TreeDocument(
    Guid Id,
    Guid? FolderId,
    string FileName,
    string ContentType,
    long SizeBytes,
    string Status,
    int ChunkCount,
    DateTimeOffset UploadedAt,
    string? FailureReason);
