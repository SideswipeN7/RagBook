using RagBook.Modules.Documents.Errors;
using RagBook.Shared.Results;

namespace RagBook.Modules.Documents.Domain;

/// <summary>
/// The current standing of a session's quota — used count and used bytes measured against
/// <see cref="QuotaLimits"/>. The single home of every boundary rule: <see cref="CanAdmit"/> is used
/// by both the cheap pre-check and the authoritative in-lock admit, so the two can never drift.
/// Storage is compared in bytes; megabytes are decimal (1 MB = 1,000,000 bytes) for display only.
/// </summary>
/// <param name="UsedDocuments">Quota-counting documents currently held (excludes demo; includes failed).</param>
/// <param name="UsedBytes">Total size of those documents, in bytes.</param>
/// <param name="Limits">The ceilings to compare against.</param>
public sealed record QuotaSnapshot(int UsedDocuments, long UsedBytes, QuotaLimits Limits)
{
    private const long BytesPerMb = 1_000_000L;

    /// <summary>Used storage in decimal megabytes, rounded to one decimal place (display).</summary>
    public double UsedMb => Math.Round((double)UsedBytes / BytesPerMb, 1);

    /// <summary>The total-storage ceiling in decimal megabytes (display).</summary>
    public double MaxTotalMb => (double)Limits.MaxTotalBytes / BytesPerMb;

    /// <summary>Bytes still available before the total-storage ceiling (never negative).</summary>
    public long RemainingBytes => Math.Max(0, Limits.MaxTotalBytes - UsedBytes);

    /// <summary>True when either the count or the storage ceiling is reached.</summary>
    public bool IsFull => UsedDocuments >= Limits.MaxDocuments || RemainingBytes == 0;

    /// <summary>True when another upload could be admitted (subject to its individual size).</summary>
    public bool CanUpload => !IsFull;

    /// <summary>
    /// Decides whether a file of <paramref name="fileSizeBytes"/> may be admitted. Only crossing a
    /// limit is rejected — usage landing exactly at a limit is admitted. Order: per-file size, then
    /// document count, then total storage.
    /// </summary>
    /// <param name="fileSizeBytes">Size of the candidate file, in bytes.</param>
    /// <returns>Success, or a failure carrying the specific quota error code.</returns>
    public Result CanAdmit(long fileSizeBytes)
    {
        if (fileSizeBytes > Limits.MaxFileSizeBytes)
        {
            return Result.Failure(QuotaErrors.FileTooLarge(Limits.MaxFileSizeBytes));
        }

        if (UsedDocuments >= Limits.MaxDocuments)
        {
            return Result.Failure(QuotaErrors.QuotaExceeded);
        }

        if (UsedBytes + fileSizeBytes > Limits.MaxTotalBytes)
        {
            return Result.Failure(QuotaErrors.TotalSizeExceeded(RemainingBytes));
        }

        return Result.Success();
    }
}
