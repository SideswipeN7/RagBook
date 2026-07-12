namespace RagBook.Modules.Documents.Domain;

/// <summary>
/// Generates embedding vectors for chunk texts through a **centralized** model (US-06): one model for the
/// whole index, so indexing and querying (US-14) are comparable. Behind this abstraction sit a real
/// provider driver and a deterministic stand-in (dev/tests). Callers embed in batches; a transient
/// provider failure surfaces as <c>EmbeddingProviderException</c> for the retry policy.
/// </summary>
public interface IEmbeddingProvider
{
    /// <summary>The vector dimension this model produces (config-driven; the whole index shares it).</summary>
    int Dimension { get; }

    /// <summary>Embeds a batch of texts, returning one vector per input in the same order.</summary>
    Task<IReadOnlyList<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken);
}
