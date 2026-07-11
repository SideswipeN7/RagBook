using FluentAssertions;
using Microsoft.Extensions.Options;
using RagBook.Infrastructure.SharedContext.Processing;
using RagBook.Modules.Documents.Domain;
using RagBook.Modules.Documents.Processing;
using Xunit;

namespace RagBook.Api.IntegrationTests.Processing;

/// <summary>Pure unit tests for the deterministic stand-in embedding provider (no Docker needed).</summary>
public sealed class FakeEmbeddingProviderTests
{
    private static FakeEmbeddingProvider CreateSut(int dimension = 1024)
    {
        return new FakeEmbeddingProvider(Options.Create(new EmbeddingOptions { Dimension = dimension }));
    }

    [Fact]
    public async Task Should_ProduceDeterministicVectorOfConfiguredDimension()
    {
        // Arrange
        IEmbeddingProvider sut = CreateSut(dimension: 256);

        // Act
        IReadOnlyList<float[]> first = await sut.EmbedBatchAsync(["hello world"], CancellationToken.None);
        IReadOnlyList<float[]> second = await sut.EmbedBatchAsync(["hello world"], CancellationToken.None);

        // Assert — same text → same vector, of the configured dimension.
        sut.Dimension.Should().Be(256);
        first[0].Should().HaveCount(256);
        first[0].Should().Equal(second[0]);
    }

    [Fact]
    public async Task Should_ReturnOneVectorPerInput_And_DifferForDifferentTexts()
    {
        // Arrange
        IEmbeddingProvider sut = CreateSut(dimension: 64);

        // Act
        IReadOnlyList<float[]> vectors = await sut.EmbedBatchAsync(["alpha", "beta", "gamma"], CancellationToken.None);

        // Assert
        vectors.Should().HaveCount(3);
        vectors[0].Should().NotEqual(vectors[1]);
        vectors[1].Should().NotEqual(vectors[2]);
    }
}
