using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using RagBook.Modules.Chat.Domain;
using RagBook.Modules.Chat.Errors;
using RagBook.Modules.Documents.Domain;
using RagBook.Modules.Folders.Domain;
using RagBook.Shared.Results;
using RagBook.Shared.Sessions;
using Xunit;

namespace RagBook.Api.IntegrationTests.Chat;

/// <summary>
/// Acceptance tests for <see cref="IScopedRetriever"/> against the real host + Dockerized pgvector (US-13).
/// The deterministic fake embedding provider makes seeded chunks and queries comparable, so these assert
/// scope membership, ready-only + session isolation, the TopK bound + ordering, page-number surfacing, and
/// the not-found failure.
/// </summary>
public sealed class ScopedRetrievalTests(RagBookApiFactory factory) : IClassFixture<RagBookApiFactory>
{
    private static readonly (string Text, int? Page)[] OneChunk = [("alpha contract clause", null)];

    private async Task<Result<ScopedRetrievalResult>> RetrieveAsync(Guid sessionId, ChatScope scope, string question)
    {
        using var scopeContainer = factory.Services.CreateScope();
        scopeContainer.ServiceProvider.GetRequiredService<ISessionInitializer>().Initialize(sessionId);
        var retriever = scopeContainer.ServiceProvider.GetRequiredService<IScopedRetriever>();

        return await retriever.RetrieveAsync(scope, question, CancellationToken.None);
    }

    [Fact]
    public async Task Should_ReturnSubtreePassages_When_FolderScope()
    {
        // Arrange — parent "Umowy" with a subfolder "Umowy/2026", each holding a ready indexed document.
        var session = Guid.NewGuid();
        Folder umowy = await ChatRetrievalSeed.SeedRootFolderAsync(factory, session, "Umowy");
        Folder umowy2026 = await ChatRetrievalSeed.SeedChildFolderAsync(factory, session, umowy, "2026");
        Guid parentDoc = await ChatRetrievalSeed.SeedReadyDocumentAsync(factory, session, "parent.pdf", umowy.Id, [("alpha in parent", 1)]);
        Guid childDoc = await ChatRetrievalSeed.SeedReadyDocumentAsync(factory, session, "child.pdf", umowy2026.Id, [("beta in subfolder", null)]);

        // Act
        Result<ScopedRetrievalResult> result = await RetrieveAsync(session, ChatScope.Folder(umowy.Id), "alpha");

        // Assert — the subtree (parent + subfolder) contributes.
        result.IsSuccess.Should().BeTrue();
        result.Value.IsEmptyScope.Should().BeFalse();
        result.Value.Matches.Select(match => match.DocumentId).Should().Contain([parentDoc, childDoc]);
    }

    [Fact]
    public async Task Should_ExcludeSiblingFolder_When_FolderScope()
    {
        // Arrange
        var session = Guid.NewGuid();
        Folder umowy = await ChatRetrievalSeed.SeedRootFolderAsync(factory, session, "Umowy");
        Folder faktury = await ChatRetrievalSeed.SeedRootFolderAsync(factory, session, "Faktury");
        Guid umowyDoc = await ChatRetrievalSeed.SeedReadyDocumentAsync(factory, session, "umowa.pdf", umowy.Id, OneChunk);
        Guid fakturyDoc = await ChatRetrievalSeed.SeedReadyDocumentAsync(factory, session, "faktura.pdf", faktury.Id, [("gamma invoice", null)]);

        // Act
        Result<ScopedRetrievalResult> result = await RetrieveAsync(session, ChatScope.Folder(umowy.Id), "alpha");

        // Assert
        result.Value.Matches.Select(match => match.DocumentId).Should().Contain(umowyDoc).And.NotContain(fakturyDoc);
    }

    [Fact]
    public async Task Should_ReturnOnlyThatDocument_When_DocumentScope()
    {
        // Arrange
        var session = Guid.NewGuid();
        Guid docA = await ChatRetrievalSeed.SeedReadyDocumentAsync(factory, session, "a.pdf", null, [("alpha one", null), ("alpha two", null)]);
        await ChatRetrievalSeed.SeedReadyDocumentAsync(factory, session, "b.pdf", null, [("beta one", null)]);

        // Act
        Result<ScopedRetrievalResult> result = await RetrieveAsync(session, ChatScope.Document(docA), "alpha");

        // Assert — every passage belongs to A.
        result.Value.Matches.Should().NotBeEmpty();
        result.Value.Matches.Should().OnlyContain(match => match.DocumentId == docA);
    }

    [Fact]
    public async Task Should_SurfacePageNumber_When_ChunkHasPage()
    {
        // Arrange (C1) — one chunk with a page, one without.
        var session = Guid.NewGuid();
        Guid doc = await ChatRetrievalSeed.SeedReadyDocumentAsync(factory, session, "paged.pdf", null, [("with page", 5), ("without page", null)]);

        // Act
        Result<ScopedRetrievalResult> result = await RetrieveAsync(session, ChatScope.Document(doc), "with page");

        // Assert — the page/location survives the raw-SQL mapping (forward-looking for US-16 citations).
        result.Value.Matches.Single(match => match.Text == "with page").PageNumber.Should().Be(5);
        result.Value.Matches.Single(match => match.Text == "without page").PageNumber.Should().BeNull();
    }

    [Fact]
    public async Task Should_ExcludeProcessingAndFailed_When_AllScope()
    {
        // Arrange — a ready doc with chunks and a FAILED doc that also has chunks (must be excluded).
        var session = Guid.NewGuid();
        Guid readyDoc = await ChatRetrievalSeed.SeedReadyDocumentAsync(factory, session, "ready.pdf", null, OneChunk);
        Guid failedDoc = await ChatRetrievalSeed.SeedDocumentWithChunksAsync(factory, session, "failed.pdf", null, DocumentStatus.Failed, [("alpha failed", null)]);

        // Act
        Result<ScopedRetrievalResult> result = await RetrieveAsync(session, ChatScope.All(), "alpha");

        // Assert — only the ready document contributes.
        result.Value.Matches.Select(match => match.DocumentId).Should().Contain(readyDoc).And.NotContain(failedDoc);
    }

    [Fact]
    public async Task Should_ExcludeOtherSession_When_AllScope()
    {
        // Arrange — two sessions each with a ready document.
        var sessionA = Guid.NewGuid();
        var sessionB = Guid.NewGuid();
        Guid docA = await ChatRetrievalSeed.SeedReadyDocumentAsync(factory, sessionA, "a.pdf", null, [("alpha in A", null)]);
        Guid docB = await ChatRetrievalSeed.SeedReadyDocumentAsync(factory, sessionB, "b.pdf", null, [("alpha in B", null)]);

        // Act — session B retrieves in the All scope.
        Result<ScopedRetrievalResult> result = await RetrieveAsync(sessionB, ChatScope.All(), "alpha");

        // Assert — B never sees A's chunks (isolation).
        result.Value.Matches.Select(match => match.DocumentId).Should().Contain(docB).And.NotContain(docA);
    }

    [Fact]
    public async Task Should_CapAtTopK_And_OrderByDistance_When_ManyMatches()
    {
        // Arrange — one document with 12 distinct chunks (> TopK = 8).
        var session = Guid.NewGuid();
        (string, int?)[] many = Enumerable.Range(0, 12).Select(index => ($"passage number {index}", (int?)null)).ToArray();
        await ChatRetrievalSeed.SeedReadyDocumentAsync(factory, session, "many.pdf", null, many);

        // Act
        Result<ScopedRetrievalResult> result = await RetrieveAsync(session, ChatScope.All(), "passage number 0");

        // Assert — capped at TopK and ordered closest-first.
        result.Value.Matches.Should().HaveCount(8);
        result.Value.Matches.Should().BeInAscendingOrder(match => match.Distance);
    }

    [Fact]
    public async Task Should_ReturnAllMatches_When_FewerThanTopK()
    {
        // Arrange (A1) — 3 eligible chunks, fewer than TopK = 8.
        var session = Guid.NewGuid();
        await ChatRetrievalSeed.SeedReadyDocumentAsync(factory, session, "few.pdf", null, [("one", null), ("two", null), ("three", null)]);

        // Act
        Result<ScopedRetrievalResult> result = await RetrieveAsync(session, ChatScope.All(), "one");

        // Assert — all eligible are returned, no padding.
        result.Value.Matches.Should().HaveCount(3);
    }

    [Fact]
    public async Task Should_Return_ScopeNotFound_When_FolderFromAnotherSession()
    {
        // Arrange — session A owns a folder; session B scopes to it.
        var sessionA = Guid.NewGuid();
        var sessionB = Guid.NewGuid();
        Folder folderA = await ChatRetrievalSeed.SeedRootFolderAsync(factory, sessionA, "Umowy");

        // Act
        Result<ScopedRetrievalResult> result = await RetrieveAsync(sessionB, ChatScope.Folder(folderA.Id), "alpha");

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(ChatErrors.ScopeNotFound);
    }

    [Fact]
    public async Task Should_Return_ScopeNotFound_When_DocumentMissing()
    {
        // Arrange — a document id that does not exist in the session.
        var session = Guid.NewGuid();

        // Act
        Result<ScopedRetrievalResult> result = await RetrieveAsync(session, ChatScope.Document(Guid.NewGuid()), "alpha");

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(ChatErrors.ScopeNotFound);
    }
}
