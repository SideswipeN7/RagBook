using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using RagBook.Modules.Chat.Domain;
using RagBook.Shared.Results;
using RagBook.Shared.Sessions;
using Xunit;

namespace RagBook.Api.IntegrationTests.Chat;

/// <summary>
/// Proves the empty-scope short-circuit (US-13 AC-5, SC-004): a scope with no ready-indexed content
/// returns <see cref="ScopedRetrievalResult.IsEmptyScope"/> **without** embedding the question, while a
/// non-empty scope embeds exactly once. Uses <see cref="ChatRetrievalApiFactory"/>'s counting provider.
/// </summary>
public sealed class EmptyScopeTests(ChatRetrievalApiFactory factory) : IClassFixture<ChatRetrievalApiFactory>
{
    private async Task<Result<ScopedRetrievalResult>> RetrieveAsync(Guid sessionId, ChatScope scope, string question)
    {
        using var scopeContainer = factory.Services.CreateScope();
        scopeContainer.ServiceProvider.GetRequiredService<ISessionInitializer>().Initialize(sessionId);
        var retriever = scopeContainer.ServiceProvider.GetRequiredService<IScopedRetriever>();

        return await retriever.RetrieveAsync(scope, question, CancellationToken.None);
    }

    [Fact]
    public async Task Should_ReturnEmptyScope_Without_Embedding_When_NoReadyDocs()
    {
        // Arrange — a folder whose only document is still processing (no chunks).
        var session = Guid.NewGuid();
        var folder = await ChatRetrievalSeed.SeedRootFolderAsync(factory, session, "Pusty");
        await ChatRetrievalSeed.SeedProcessingDocumentAsync(factory, session, "processing.pdf", folder.Id);

        // Act — measure embedding calls only around the retrieval.
        int before = factory.Embeddings.Calls;
        Result<ScopedRetrievalResult> result = await RetrieveAsync(session, ChatScope.Folder(folder.Id), "alpha");

        // Assert — empty outcome, and the question was NOT embedded.
        result.IsSuccess.Should().BeTrue();
        result.Value.IsEmptyScope.Should().BeTrue();
        result.Value.Matches.Should().BeEmpty();
        factory.Embeddings.Calls.Should().Be(before);
    }

    [Fact]
    public async Task Should_EmbedOnce_When_ScopeHasReadyContent()
    {
        // Arrange — a folder with a ready, indexed document.
        var session = Guid.NewGuid();
        var folder = await ChatRetrievalSeed.SeedRootFolderAsync(factory, session, "Umowy");
        await ChatRetrievalSeed.SeedReadyDocumentAsync(factory, session, "ready.pdf", folder.Id, [("alpha clause", null)]);

        // Act
        int before = factory.Embeddings.Calls;
        Result<ScopedRetrievalResult> result = await RetrieveAsync(session, ChatScope.Folder(folder.Id), "alpha");

        // Assert — non-empty, and the question was embedded exactly once.
        result.Value.IsEmptyScope.Should().BeFalse();
        (factory.Embeddings.Calls - before).Should().Be(1);
    }
}
