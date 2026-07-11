using System.Text;
using FluentAssertions;
using RagBook.Api.IntegrationTests.Documents;
using RagBook.Api.IntegrationTests.Folders;
using RagBook.Modules.Tree.Features.GetTree;
using Xunit;

namespace RagBook.Api.IntegrationTests.Tree;

/// <summary>
/// Acceptance tests for <c>GET /api/tree</c> against the real host + Dockerized PostgreSQL. Each test
/// uses a fresh session id; folders/documents are isolated by the global query filter.
/// </summary>
public sealed class TreeEndpointTests(RagBookApiFactory factory) : IClassFixture<RagBookApiFactory>
{
    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.7\nx\n");

    private FolderApiClient Folders(Guid s) => new(factory, s);

    private DocumentApiClient Documents(Guid s) => new(factory, s);

    private TreeApiClient Tree(Guid s) => new(factory, s);

    [Fact]
    public async Task Should_ComposeFoldersAndDocuments_InOneResponse()
    {
        // Arrange (AC-5) — one folder, a document inside it, and a document at the root.
        var sessionId = Guid.NewGuid();
        var folder = await Folders(sessionId).CreateAsync("Umowy", null);
        await Documents(sessionId).UploadAsync(Pdf, "in-folder.pdf", "application/pdf", folder.Id);
        await Documents(sessionId).UploadAsync(Pdf, "root.pdf", "application/pdf", folderId: null);

        // Act
        TreeResponse tree = await Tree(sessionId).GetAsync();

        // Assert — both lists arrive in one call; documents are placed by folderId.
        tree.Folders.Should().ContainSingle(f => f.Id == folder.Id && f.Name == "Umowy" && f.Depth == 1);
        tree.Documents.Should().HaveCount(2);
        tree.Documents.Single(d => d.FileName == "in-folder.pdf").FolderId.Should().Be(folder.Id);
        tree.Documents.Single(d => d.FileName == "root.pdf").FolderId.Should().BeNull();
    }

    [Fact]
    public async Task Should_OrderFoldersAlphabetically()
    {
        // Arrange (FR-008)
        var sessionId = Guid.NewGuid();
        await Folders(sessionId).CreateAsync("banan", null);
        await Folders(sessionId).CreateAsync("Ananas", null);
        await Folders(sessionId).CreateAsync("czekolada", null);

        // Act
        TreeResponse tree = await Tree(sessionId).GetAsync();

        // Assert
        tree.Folders.Select(f => f.Name).Should().ContainInOrder("Ananas", "banan", "czekolada");
    }

    [Fact]
    public async Task Should_ExcludeOtherSessionsData()
    {
        // Arrange (FR-012)
        var owner = Guid.NewGuid();
        var intruder = Guid.NewGuid();
        var folder = await Folders(owner).CreateAsync("Umowy", null);
        await Documents(owner).UploadAsync(Pdf, "secret.pdf", "application/pdf", folder.Id);

        // Act
        TreeResponse tree = await Tree(intruder).GetAsync();

        // Assert — the intruder sees nothing.
        tree.Folders.Should().BeEmpty();
        tree.Documents.Should().BeEmpty();
    }

    [Fact]
    public async Task Should_ExcludeDemoDocuments()
    {
        // Arrange (FR-013) — one user upload + one demo document.
        var sessionId = Guid.NewGuid();
        await Documents(sessionId).UploadAsync(Pdf, "mine.pdf", "application/pdf", folderId: null);
        await TreeSeed.SeedDemoDocumentAsync(factory, sessionId);

        // Act
        TreeResponse tree = await Tree(sessionId).GetAsync();

        // Assert — only the user document is in the session tree.
        tree.Documents.Should().ContainSingle(d => d.FileName == "mine.pdf");
    }

    [Fact]
    public async Task Should_SurfaceFailureReason_ForFailedDocument()
    {
        // Arrange — a failed document with a reason (stands in for US-06's write).
        var sessionId = Guid.NewGuid();
        await TreeSeed.SeedFailedDocumentAsync(factory, sessionId, "broken.pdf", "Encrypted PDF");

        // Act (AC-2 / failure_reason round-trip)
        TreeResponse tree = await Tree(sessionId).GetAsync();

        // Assert
        var failed = tree.Documents.Single(d => d.FileName == "broken.pdf");
        failed.Status.Should().Be("Failed");
        failed.FailureReason.Should().Be("Encrypted PDF");
    }

    [Fact]
    public async Task Should_ReturnEmpty_When_FreshSession()
    {
        // Arrange (AC-3 data side — the empty state is rendered by the client)
        var sessionId = Guid.NewGuid();

        // Act
        TreeResponse tree = await Tree(sessionId).GetAsync();

        // Assert
        tree.Folders.Should().BeEmpty();
        tree.Documents.Should().BeEmpty();
    }
}
