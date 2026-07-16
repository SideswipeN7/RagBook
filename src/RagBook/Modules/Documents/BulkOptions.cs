namespace RagBook.Modules.Documents;

/// <summary>
/// Configuration for bulk operations (US-12). Bound from the <c>Bulk</c> section so the list-size cap is
/// config-driven — no magic numbers (constitution §VII). "Quota-ready": raising the cap for a future tier is a
/// config edit only, no code change.
/// </summary>
public sealed class BulkOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Bulk";

    /// <summary>Maximum number of ids a single bulk request may carry (after de-duplication); over-cap → 400.</summary>
    public int MaxItems { get; set; } = 50;
}
