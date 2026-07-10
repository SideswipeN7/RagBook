using RagBook.Shared.Results;

namespace RagBook.Modules.Documents.Domain;

/// <summary>
/// Persistence seam for the upload write path (US-04). It reuses the US-05 session advisory lock so the
/// quota admit stays atomic, and — because that lock serializes a session's uploads — computes the
/// first free file name for the target folder under the lock before a single insert (research D5). The
/// two partial unique file-name indexes are a backstop, not a retry loop.
/// </summary>
public interface IDocumentUploadRepository
{
    /// <summary>
    /// Under the session lock: re-checks the quota (returning a quota failure without inserting when it
    /// would be breached), assigns the first free suffixed name in the target folder, and inserts the
    /// document. Guarantees the count/total quota holds and the name is unique under concurrency.
    /// </summary>
    Task<Result> AddUploadedWithinQuotaAsync(Document document, QuotaLimits limits, CancellationToken cancellationToken);
}
