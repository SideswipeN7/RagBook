using Microsoft.Extensions.Options;
using RagBook.Infrastructure.SharedContext.Processing;
using RagBook.Modules.Documents.Domain;
using RagBook.Modules.Documents.Processing;

namespace RagBook.Api.IntegrationTests.Chat;

/// <summary>
/// <see cref="IEmbeddingProvider"/> that delegates to the deterministic <see cref="FakeEmbeddingProvider"/>
/// (same vectors, so seeded chunks and queries stay comparable) while counting <see cref="EmbedBatchAsync"/>
/// calls — so the empty-scope test can assert the retriever did **not** embed the question (US-13 AC-5).
/// </summary>
public sealed class CountingEmbeddingProvider(IOptions<EmbeddingOptions> options) : IEmbeddingProvider
{
    private readonly FakeEmbeddingProvider _inner = new(options);
    private int _calls;

    /// <summary>How many times <see cref="EmbedBatchAsync"/> has been invoked.</summary>
    public int Calls => _calls;

    /// <inheritdoc />
    public int Dimension => _inner.Dimension;

    /// <inheritdoc />
    public Task<IReadOnlyList<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _calls);

        return _inner.EmbedBatchAsync(texts, cancellationToken);
    }
}
