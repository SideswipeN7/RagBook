using Microsoft.Extensions.Options;
using RagBook.Modules.Documents.Domain;
using RagBook.Modules.Documents.Processing;

namespace RagBook.Infrastructure.SharedContext.Processing;

/// <summary>
/// Deterministic stand-in <see cref="IEmbeddingProvider"/> for dev/tests (US-06), used when no provider
/// key is configured. Produces a stable unit vector of the configured dimension from a hash of each text
/// — same text → same vector, so the index is reproducible and query-comparable without a real provider.
/// </summary>
public sealed class FakeEmbeddingProvider(IOptions<EmbeddingOptions> options) : IEmbeddingProvider
{
    /// <inheritdoc />
    public int Dimension => options.Value.Dimension;

    /// <inheritdoc />
    public Task<IReadOnlyList<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken)
    {
        IReadOnlyList<float[]> vectors = texts.Select(Embed).ToList();

        return Task.FromResult(vectors);
    }

    private float[] Embed(string text)
    {
        int dimension = Dimension;
        var vector = new float[dimension];

        // Seed a stable PRNG from the text (FNV-1a); fill and normalize to a unit vector.
        ulong state = Fnv1a(text);
        for (int i = 0; i < dimension; i++)
        {
            state = (state * 6364136223846793005UL) + 1442695040888963407UL;
            vector[i] = ((state >> 33) / (float)uint.MaxValue) - 0.5f;
        }

        double norm = Math.Sqrt(vector.Sum(value => (double)value * value));
        if (norm > 0)
        {
            for (int i = 0; i < dimension; i++)
            {
                vector[i] = (float)(vector[i] / norm);
            }
        }

        return vector;
    }

    private static ulong Fnv1a(string text)
    {
        ulong hash = 14695981039346656037UL;
        foreach (char character in text)
        {
            hash ^= character;
            hash *= 1099511628211UL;
        }

        return hash;
    }
}
