using RagBook.Shared.Results;

namespace RagBook.Modules.Documents.Errors;

/// <summary>
/// Closed error catalog for the Documents module's quota surface. Handlers and the quota domain may
/// only return codes from this list; codes are stable and namespaced <c>quota.*</c> /
/// <c>document.*</c> (constitution §II). The full RagBook catalog is owned by US-19; this is the
/// module's slice.
/// </summary>
public static class QuotaErrors
{
    private const long BytesPerMb = 1_000_000L;

    /// <summary>The session already holds the maximum number of documents (AC-2).</summary>
    public static readonly Error QuotaExceeded =
        Error.Conflict("quota.exceeded", "Document limit reached. Delete files to free up space.");

    /// <summary>A concurrent persistence conflict surfaced at the database boundary (infra fallback).</summary>
    public static readonly Error Conflict =
        Error.Conflict("quota.conflict", "The upload conflicted with a concurrent change. Please retry.");

    /// <summary>A document was created with an invalid (negative) size.</summary>
    public static readonly Error InvalidSize =
        Error.Validation("quota.invalid_size", "Document size must be zero or greater.");

    /// <summary>
    /// Admitting the file would exceed the total-storage limit (AC-3). The message conveys the
    /// remaining available space so the client can inform the user.
    /// </summary>
    /// <param name="remainingBytes">Bytes still available before the total limit.</param>
    public static Error TotalSizeExceeded(long remainingBytes)
    {
        return Error.Conflict(
            "quota.total_size_exceeded",
            $"Storage limit reached. Available space: {ToMb(remainingBytes)} MB.");
    }

    /// <summary>The file is larger than the configured per-file maximum.</summary>
    /// <param name="maxFileSizeBytes">The configured per-file maximum, in bytes.</param>
    public static Error FileTooLarge(long maxFileSizeBytes)
    {
        return Error.Validation(
            "quota.file_too_large",
            $"File exceeds the maximum size of {ToMb(maxFileSizeBytes)} MB.");
    }

    private static double ToMb(long bytes)
    {
        return Math.Round((double)bytes / BytesPerMb, 1);
    }
}
