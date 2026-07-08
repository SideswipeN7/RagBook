using RagBook.Shared.Results;

namespace RagBook.Modules.Documents.Domain;

/// <summary>
/// The quota enforcement point. Reads the config-driven limits and the session's current usage to
/// serve the UI state (<see cref="GetSnapshotAsync"/>), guard an upload before reading its body
/// (<see cref="CheckCanUpload"/>), and atomically admit-and-persist a document under concurrency
/// (<see cref="TryAdmitAsync"/>). The current session is ambient (via the session context /
/// global query filter), so no session id is threaded through these calls.
/// </summary>
public interface IQuotaService
{
    /// <summary>Returns the current session's quota standing for the UI counter (AC-1).</summary>
    Task<QuotaSnapshot> GetSnapshotAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Non-atomic pre-check that a file of <paramref name="fileSizeBytes"/> could be admitted
    /// (AC-2/AC-3). The authoritative check remains <see cref="TryAdmitAsync"/>.
    /// </summary>
    Task<Result> CheckCanUpload(long fileSizeBytes, CancellationToken cancellationToken);

    /// <summary>
    /// Atomically admits a document of <paramref name="fileSizeBytes"/> for the current session and
    /// returns its new identity, or a quota failure. The seam US-04's upload command calls.
    /// </summary>
    Task<Result<Guid>> TryAdmitAsync(long fileSizeBytes, DocumentOrigin origin, CancellationToken cancellationToken);
}
