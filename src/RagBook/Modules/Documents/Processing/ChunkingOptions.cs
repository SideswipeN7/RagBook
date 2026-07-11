namespace RagBook.Modules.Documents.Processing;

/// <summary>
/// Config-driven chunking parameters (US-06, no magic numbers). Bound from the <c>Chunking</c> section.
/// </summary>
public sealed class ChunkingOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Chunking";

    /// <summary>Target chunk size in characters (~800–1200).</summary>
    public int TargetChars { get; set; } = 1000;

    /// <summary>Overlap between consecutive chunks in characters (~150); skipped for a single short chunk.</summary>
    public int OverlapChars { get; set; } = 150;
}
