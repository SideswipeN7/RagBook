using RagBook.Shared.Results;

namespace RagBook.Modules.Documents.Domain;

/// <summary>
/// Persistence seam the quota reads. Every read is automatically constrained to the current session
/// by the EF Core global query filter behind the implementation, and additionally excludes
/// <see cref="DocumentOrigin.Demo"/> documents (US-05 rule). This interface exposes no way to query
/// across sessions. US-04's upload wires through <see cref="TryAddWithinQuotaAsync"/>.
/// </summary>
public interface IDocumentQuotaRepository
{
    /// <summary>Counts the current session's quota-counting documents (excludes demo; includes failed).</summary>
    Task<int> CountAsync(CancellationToken cancellationToken);

    /// <summary>Sums the byte size of the current session's quota-counting documents.</summary>
    Task<long> SumSizeBytesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Atomically admits and persists <paramref name="document"/> within <paramref name="limits"/>:
    /// under a session-scoped lock, re-reads the current usage, evaluates admission, and either inserts
    /// the document or returns a quota failure — without inserting. Guarantees at-most-one admission
    /// under concurrency (AC-5).
    /// </summary>
    Task<Result> TryAddWithinQuotaAsync(Document document, QuotaLimits limits, CancellationToken cancellationToken);
}
