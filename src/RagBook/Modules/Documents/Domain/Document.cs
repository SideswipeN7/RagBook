using RagBook.Modules.Documents.Errors;
using RagBook.Shared.Auditing;
using RagBook.Shared.Results;
using RagBook.Shared.Sessions;

namespace RagBook.Modules.Documents.Domain;

/// <summary>
/// A session-owned document. US-05 keeps this minimal — only what the quota needs to count and size
/// (<see cref="SizeBytes"/>, <see cref="Status"/>, <see cref="Origin"/>). US-04 (upload) extends the
/// same aggregate/table with filename, storage pointer, and richer processing. <see cref="UserSessionId"/>
/// is stamped centrally on insert (never in handlers); isolation is enforced at the query boundary.
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
}
