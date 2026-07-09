using Microsoft.Extensions.Options;
using RagBook.Modules.Documents.Domain;
using RagBook.Shared.Results;

namespace RagBook.Modules.Documents.Quota;

/// <summary>
/// Default <see cref="IQuotaService"/>: reads the config-driven <see cref="QuotaOptions"/> and the
/// session's current usage (via <see cref="IDocumentQuotaRepository"/>) and delegates every boundary
/// decision to the pure <see cref="QuotaSnapshot"/>, so the pre-check and the atomic admit share one
/// rule set.
/// </summary>
public sealed class QuotaService(IDocumentQuotaRepository repository, IOptions<QuotaOptions> options)
    : IQuotaService
{
    /// <inheritdoc />
    public async Task<QuotaSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        int usedDocuments = await repository.CountAsync(cancellationToken);
        long usedBytes = await repository.SumSizeBytesAsync(cancellationToken);

        return new QuotaSnapshot(usedDocuments, usedBytes, options.Value.ToLimits());
    }

    /// <inheritdoc />
    public async Task<Result> CheckCanUpload(long fileSizeBytes, CancellationToken cancellationToken)
    {
        QuotaSnapshot snapshot = await GetSnapshotAsync(cancellationToken);

        return snapshot.CanAdmit(fileSizeBytes);
    }

    /// <inheritdoc />
    public async Task<Result<Guid>> TryAdmitAsync(
        long fileSizeBytes,
        DocumentOrigin origin,
        CancellationToken cancellationToken)
    {
        Result<Document> creation = Document.CreateForQuota(fileSizeBytes, origin);
        if (creation.IsFailure)
        {
            return Result.Failure<Guid>(creation.Error);
        }

        Document document = creation.Value;
        Result admission = await repository.TryAddWithinQuotaAsync(document, options.Value.ToLimits(), cancellationToken);
        if (admission.IsFailure)
        {
            return Result.Failure<Guid>(admission.Error);
        }

        return document.Id;
    }
}
