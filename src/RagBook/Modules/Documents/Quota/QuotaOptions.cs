using RagBook.Modules.Documents.Domain;

namespace RagBook.Modules.Documents.Quota;

/// <summary>
/// Configuration for the per-session file quota. Bound from the <c>Quota</c> section so every limit is
/// config-driven — no magic numbers (constitution §VII). Defaults model the free tier; "quota-ready"
/// means raising a tier is a config edit only.
/// </summary>
public sealed class QuotaOptions
{
    private const long BytesPerMb = 1_000_000L;

    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Quota";

    /// <summary>Maximum number of quota-counting documents per session.</summary>
    public int MaxDocuments { get; set; } = 10;

    /// <summary>Maximum size of a single file, in megabytes (decimal).</summary>
    public int MaxFileSizeMb { get; set; } = 10;

    /// <summary>Maximum total storage per session, in megabytes (decimal).</summary>
    public int MaxTotalMb { get; set; } = 50;

    /// <summary>Projects the megabyte-configured limits into the byte-based <see cref="QuotaLimits"/>.</summary>
    public QuotaLimits ToLimits()
    {
        return new QuotaLimits(MaxDocuments, MaxFileSizeMb * BytesPerMb, MaxTotalMb * BytesPerMb);
    }
}
