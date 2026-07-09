using RagBook.Modules.Documents.Domain;

namespace RagBook.Modules.Documents.Features.GetQuota;

/// <summary>
/// Read model for the quota-bar. Reports usage against the configured limits so the UI can render
/// "X / N plików" and "X / N MB" and disable the upload control when full.
/// </summary>
/// <param name="UsedDocuments">Quota-counting documents currently held.</param>
/// <param name="MaxDocuments">The document-count ceiling.</param>
/// <param name="UsedMb">Used storage in decimal megabytes (rounded to one decimal).</param>
/// <param name="MaxTotalMb">The total-storage ceiling in decimal megabytes.</param>
/// <param name="MaxFileSizeMb">The per-file size ceiling in decimal megabytes.</param>
/// <param name="CanUpload">Whether another upload could currently be admitted.</param>
public sealed record QuotaStateResponse(
    int UsedDocuments,
    int MaxDocuments,
    double UsedMb,
    double MaxTotalMb,
    int MaxFileSizeMb,
    bool CanUpload)
{
    private const long BytesPerMb = 1_000_000L;

    /// <summary>Projects a domain <see cref="QuotaSnapshot"/> into the client read model.</summary>
    public static QuotaStateResponse From(QuotaSnapshot snapshot)
    {
        return new QuotaStateResponse(
            snapshot.UsedDocuments,
            snapshot.Limits.MaxDocuments,
            snapshot.UsedMb,
            snapshot.MaxTotalMb,
            (int)(snapshot.Limits.MaxFileSizeBytes / BytesPerMb),
            snapshot.CanUpload);
    }
}
