using RagBook.Modules.Documents.Domain;

namespace RagBook.Modules.Documents.Features.GetQuota;

/// <summary>
/// Handles <see cref="GetQuotaQuery"/>. The snapshot is scoped to the current session by the quota
/// service (global query filter), so it never reflects another session's documents (AC-1, FR-006).
/// </summary>
public sealed class GetQuotaQueryHandler(IQuotaService quotaService)
{
    /// <summary>Returns the current session's quota state as a read model.</summary>
    public async Task<QuotaStateResponse> Handle(GetQuotaQuery query, CancellationToken cancellationToken)
    {
        QuotaSnapshot snapshot = await quotaService.GetSnapshotAsync(cancellationToken);

        return QuotaStateResponse.From(snapshot);
    }
}
