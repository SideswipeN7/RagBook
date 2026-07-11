namespace RagBook.Modules.Documents.Processing;

/// <summary>
/// Config-driven embedding parameters (US-06, no magic numbers). Bound from the <c>Embedding</c> section.
/// One model for the whole index; <see cref="ApiKey"/> present selects the real provider, absent selects
/// the deterministic stand-in (dev/tests). Changing <see cref="Model"/>/<see cref="Dimension"/> means a
/// full re-index (see README).
/// </summary>
public sealed class EmbeddingOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Embedding";

    /// <summary>The embedding model id (the whole index shares it).</summary>
    public string Model { get; set; } = "voyage-3.5";

    /// <summary>The produced vector dimension (must match the <c>vector(N)</c> column).</summary>
    public int Dimension { get; set; } = 1024;

    /// <summary>Number of chunk texts per provider call (batched, not per-chunk).</summary>
    public int BatchSize { get; set; } = 64;

    /// <summary>Bounded retry attempts for transient provider failures.</summary>
    public int RetryCount { get; set; } = 3;

    /// <summary>The provider API key; when empty, the deterministic stand-in is used (dev/tests).</summary>
    public string? ApiKey { get; set; }
}
