namespace RagBook.Modules.Documents.Domain;

/// <summary>
/// The per-session quota ceilings, in the units the domain compares against (bytes). Projected from
/// <c>QuotaOptions</c> (config-driven, no magic numbers). "Quota-ready": raising a tier is a config
/// edit that produces different limits here, with no code change.
/// </summary>
/// <param name="MaxDocuments">Maximum number of quota-counting documents per session.</param>
/// <param name="MaxFileSizeBytes">Maximum size of a single file, in bytes.</param>
/// <param name="MaxTotalBytes">Maximum total storage per session, in bytes.</param>
public sealed record QuotaLimits(int MaxDocuments, long MaxFileSizeBytes, long MaxTotalBytes);
