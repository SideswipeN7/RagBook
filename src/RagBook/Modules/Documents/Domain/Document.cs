using RagBook.Modules.Documents.Errors;
using RagBook.Shared.Auditing;
using RagBook.Shared.Results;
using RagBook.Shared.Sessions;

namespace RagBook.Modules.Documents.Domain;

/// <summary>
/// A session-owned document. US-05 introduced it minimally (<see cref="SizeBytes"/>,
/// <see cref="Status"/>, <see cref="Origin"/> — what the quota counts). US-04 (upload) extends the same
/// aggregate/table with the folder association and file metadata (<see cref="FolderId"/>,
/// <see cref="FileName"/>, <see cref="ContentType"/>, <see cref="StoragePath"/>,
/// <see cref="UploadedAt"/>, <see cref="ChunkCount"/>). <see cref="UserSessionId"/> is stamped centrally
/// on insert (never in handlers); isolation is enforced at the query boundary.
/// </summary>
public sealed class Document : ISessionOwned, IAuditable
{
    private Document(Guid id, long sizeBytes, DocumentStatus status, DocumentOrigin origin)
    {
        Id = id;
        SizeBytes = sizeBytes;
        Status = status;
        Origin = origin;
    }

    private Document(
        Guid id,
        long sizeBytes,
        Guid? folderId,
        string fileName,
        string contentType,
        string storagePath,
        DateTimeOffset uploadedAt)
    {
        Id = id;
        SizeBytes = sizeBytes;
        Status = DocumentStatus.Processing;
        Origin = DocumentOrigin.User;
        FolderId = folderId;
        FileName = fileName;
        ContentType = contentType;
        StoragePath = storagePath;
        UploadedAt = uploadedAt;
        ChunkCount = 0;
    }

    // Required by EF Core for materialization.
    private Document()
    {
    }

    /// <summary>Identity (GUID v4).</summary>
    public Guid Id { get; private set; }

    /// <summary>File size in bytes — the quota's storage unit.</summary>
    public long SizeBytes { get; private set; }

    /// <summary>Processing lifecycle state; <see cref="DocumentStatus.Failed"/> still counts toward quota.</summary>
    public DocumentStatus Status { get; private set; }

    /// <summary>How the document entered the session; <see cref="DocumentOrigin.Demo"/> is excluded from quota.</summary>
    public DocumentOrigin Origin { get; private set; }

    /// <summary>Owning folder, or <c>null</c> for a root document (US-04 AC-4). Null on US-05-minimal rows.</summary>
    public Guid? FolderId { get; private set; }

    /// <summary>Display file name incl. extension, post duplicate-suffix (US-04 AC-5). Null on US-05-minimal rows.</summary>
    public string? FileName { get; private set; }

    /// <summary>Detected canonical content type (never the client-declared value). Null on US-05-minimal rows.</summary>
    public string? ContentType { get; private set; }

    /// <summary>Opaque pointer to the stored binary (outside the relational DB). Null on US-05-minimal rows.</summary>
    public string? StoragePath { get; private set; }

    /// <summary>When the upload was recorded (stamped via <c>TimeProvider</c>). Null on US-05-minimal rows.</summary>
    public DateTimeOffset? UploadedAt { get; private set; }

    /// <summary>Number of chunks produced by background processing (US-06); zero at upload.</summary>
    public int ChunkCount { get; private set; }

    /// <summary>
    /// Human-readable reason a document's processing failed. Null unless <see cref="Status"/> is
    /// <see cref="DocumentStatus.Failed"/> with a recorded reason. Added by US-07 (display only);
    /// <b>populated by US-06</b> on a failed transition.
    /// </summary>
    public string? FailureReason { get; private set; }

    /// <inheritdoc />
    public Guid UserSessionId { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset CreatedAt { get; set; }

    /// <inheritdoc />
    public string CreatedBy { get; set; } = string.Empty;

    /// <inheritdoc />
    public DateTimeOffset? ModifiedAt { get; set; }

    /// <inheritdoc />
    public string? ModifiedBy { get; set; }

    /// <summary>
    /// Creates a new document for quota admission. It starts in <see cref="DocumentStatus.Processing"/>;
    /// per-file and total size limits are enforced by the quota (config-driven), not by this factory,
    /// which only guards the intrinsic size invariant. Returns a failed result rather than throwing.
    /// </summary>
    /// <param name="sizeBytes">File size in bytes (must be zero or greater).</param>
    /// <param name="origin">Whether the document is a user upload or a demo document.</param>
    public static Result<Document> CreateForQuota(long sizeBytes, DocumentOrigin origin)
    {
        if (sizeBytes < 0)
        {
            return QuotaErrors.InvalidSize;
        }

        return new Document(Guid.NewGuid(), sizeBytes, DocumentStatus.Processing, origin);
    }

    /// <summary>
    /// Creates a document from a completed upload (US-04): <see cref="DocumentStatus.Processing"/>,
    /// <see cref="DocumentOrigin.User"/>, with the folder association and file metadata. Guards the
    /// intrinsic invariants (non-empty size, non-blank name/type/storage pointer); the type, size, and
    /// quota gates run in the handler/quota admit, not here. Returns a failed result rather than throwing.
    /// </summary>
    public static Result<Document> CreateUpload(
        long sizeBytes,
        string fileName,
        string contentType,
        Guid? folderId,
        string storagePath,
        DateTimeOffset uploadedAt)
    {
        if (sizeBytes <= 0)
        {
            return DocumentErrors.EmptyFile;
        }

        if (string.IsNullOrWhiteSpace(fileName)
            || string.IsNullOrWhiteSpace(contentType)
            || string.IsNullOrWhiteSpace(storagePath))
        {
            return QuotaErrors.InvalidSize;
        }

        return new Document(Guid.NewGuid(), sizeBytes, folderId, fileName, contentType, storagePath, uploadedAt);
    }

    /// <summary>
    /// Renames the file to a de-duplicated candidate (US-04 AC-5). Used by the upload repository when it
    /// finds the first free suffix for the target folder under the session lock — before insert.
    /// </summary>
    public void RenameForSuffix(string deduplicatedFileName)
    {
        FileName = deduplicatedFileName;
    }

    /// <summary>
    /// Marks the document successfully processed (US-06): <see cref="DocumentStatus.Ready"/> with the
    /// produced <paramref name="chunkCount"/>, clearing any prior failure reason.
    /// </summary>
    public void MarkReady(int chunkCount)
    {
        Status = DocumentStatus.Ready;
        ChunkCount = chunkCount;
        FailureReason = null;
    }

    /// <summary>
    /// Marks the document failed to process (US-06): <see cref="DocumentStatus.Failed"/> with a
    /// human-readable <paramref name="reason"/> and zero chunks.
    /// </summary>
    public void MarkFailed(string reason)
    {
        Status = DocumentStatus.Failed;
        FailureReason = reason;
        ChunkCount = 0;
    }

    /// <summary>
    /// Moves the document to <paramref name="folderId"/> (US-10), or to the root when <c>null</c>. Only the
    /// owning folder changes — the indexed content (chunks/vectors) is untouched, since a folder is just an
    /// attribute of the document. Movability regardless of <see cref="Status"/> is intentional (a Processing
    /// document may be reorganised — the folder does not affect the pipeline).
    /// </summary>
    public void MoveToFolder(Guid? folderId)
    {
        FolderId = folderId;
    }
}
