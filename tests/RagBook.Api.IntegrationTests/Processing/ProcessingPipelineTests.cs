using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RagBook.Infrastructure.SharedContext.Persistence;
using RagBook.Modules.Documents.Domain;
using RagBook.Modules.Documents.Features.UploadDocument;
using RagBook.Modules.Documents.Processing;
using RagBook.Shared.Sessions;
using Xunit;

namespace RagBook.Api.IntegrationTests.Processing;

/// <summary>
/// End-to-end processing tests against the real host + Dockerized pgvector. Documents are seeded
/// directly (blob + row, without publishing the upload event) so the ONLY processing is our explicit
/// direct handler call — the real upload path also dispatches the Wolverine handler in-memory, which
/// would double-process. The deterministic stand-in embedding provider is used (no key). Verifies
/// indexing, cascade delete, session isolation, and idempotence.
/// </summary>
public sealed class ProcessingPipelineTests(RagBookApiFactory factory) : IClassFixture<RagBookApiFactory>
{
    private static readonly DateTimeOffset SeededAt = new(2026, 7, 11, 12, 0, 0, TimeSpan.Zero);

    private async Task<Guid> SeedDocumentAsync(Guid sessionId, byte[] content, string fileName, string contentType)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ISessionInitializer>().Initialize(sessionId);
        var storage = scope.ServiceProvider.GetRequiredService<IFileStorage>();
        var dbContext = scope.ServiceProvider.GetRequiredService<RagBookDbContext>();

        string storagePath = await storage.SaveAsync(new MemoryStream(content), fileName, CancellationToken.None);
        Document document = Document.CreateUpload(content.Length, fileName, contentType, null, storagePath, SeededAt).Value;
        dbContext.Documents.Add(document);
        await dbContext.SaveChangesAsync();

        return document.Id;
    }

    private async Task RunProcessingAsync(Guid documentId)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        var handler = new ProcessDocumentHandler(
            scope.ServiceProvider.GetRequiredService<IDocumentProcessingReader>(),
            scope.ServiceProvider.GetRequiredService<ISessionInitializer>(),
            scope.ServiceProvider.GetRequiredService<IFileStorage>(),
            scope.ServiceProvider.GetServices<ITextExtractor>(),
            scope.ServiceProvider.GetRequiredService<IChunker>(),
            scope.ServiceProvider.GetRequiredService<IEmbeddingProvider>(),
            scope.ServiceProvider.GetRequiredService<IChunkRepository>(),
            scope.ServiceProvider.GetRequiredService<IDocumentStatusNotifier>(),
            scope.ServiceProvider.GetRequiredService<IOptions<EmbeddingOptions>>());

        await handler.Handle(new DocumentUploaded(documentId), CancellationToken.None);
    }

    private async Task<T> InSessionAsync<T>(Guid sessionId, Func<RagBookDbContext, Task<T>> query)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ISessionInitializer>().Initialize(sessionId);
        var dbContext = scope.ServiceProvider.GetRequiredService<RagBookDbContext>();

        return await query(dbContext);
    }

    private static byte[] Text(string content) => Encoding.UTF8.GetBytes(content);

    [Fact]
    public async Task Should_IndexDocument_EndToEnd()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var documentId = await SeedDocumentAsync(sessionId, Text("To jest czytelny dokument. " + new string('x', 2000)), "notes.txt", "text/plain");

        // Act
        await RunProcessingAsync(documentId);

        // Assert — Ready with a chunk count, and chunks with 1024-dim vectors exist.
        Document document = await InSessionAsync(sessionId, db => db.Documents.AsNoTracking().FirstAsync(d => d.Id == documentId));
        document.Status.Should().Be(DocumentStatus.Ready);
        document.ChunkCount.Should().BeGreaterThan(0);

        int chunkCount = await InSessionAsync(sessionId, db => db.Chunks.CountAsync(c => c.DocumentId == documentId));
        chunkCount.Should().Be(document.ChunkCount);

        int dimension = await InSessionAsync(sessionId, db => db.Database
            .SqlQuery<int>($"SELECT vector_dims(embedding) AS \"Value\" FROM chunks WHERE document_id = {documentId} LIMIT 1")
            .FirstAsync());
        dimension.Should().Be(1024);
    }

    [Fact]
    public async Task Should_MarkFailed_When_EmptyText()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var documentId = await SeedDocumentAsync(sessionId, Text("   \n\t   "), "blank.txt", "text/plain");

        // Act
        await RunProcessingAsync(documentId);

        // Assert
        Document document = await InSessionAsync(sessionId, db => db.Documents.AsNoTracking().FirstAsync(d => d.Id == documentId));
        document.Status.Should().Be(DocumentStatus.Failed);
        document.FailureReason.Should().NotBeNullOrEmpty();
        (await InSessionAsync(sessionId, db => db.Chunks.CountAsync(c => c.DocumentId == documentId))).Should().Be(0);
    }

    [Fact]
    public async Task Should_CascadeDeleteChunks_When_DocumentDeleted()
    {
        // Arrange (FR-009)
        var sessionId = Guid.NewGuid();
        var documentId = await SeedDocumentAsync(sessionId, Text("Zawartosc do zaindeksowania."), "d.txt", "text/plain");
        await RunProcessingAsync(documentId);
        (await InSessionAsync(sessionId, db => db.Chunks.CountAsync(c => c.DocumentId == documentId))).Should().BeGreaterThan(0);

        // Act — delete the document (US-08 stand-in).
        await InSessionAsync(sessionId, async db =>
        {
            Document document = await db.Documents.FirstAsync(d => d.Id == documentId);
            db.Documents.Remove(document);
            await db.SaveChangesAsync();
            return true;
        });

        // Assert — chunks gone (FK ON DELETE CASCADE).
        int remaining = await InSessionAsync(sessionId, db =>
            db.Chunks.IgnoreQueryFilters().CountAsync(c => c.DocumentId == documentId));
        remaining.Should().Be(0);
    }

    [Fact]
    public async Task Should_NotExposeChunksToAnotherSession()
    {
        // Arrange (FR-014)
        var owner = Guid.NewGuid();
        var intruder = Guid.NewGuid();
        var documentId = await SeedDocumentAsync(owner, Text("Prywatna tresc do zaindeksowania."), "secret.txt", "text/plain");
        await RunProcessingAsync(documentId);
        (await InSessionAsync(owner, db => db.Chunks.CountAsync())).Should().BeGreaterThan(0);

        // Act & Assert — session B sees no chunks.
        (await InSessionAsync(intruder, db => db.Chunks.CountAsync())).Should().Be(0);
    }

    [Fact]
    public async Task Should_ProduceIdenticalChunks_When_ProcessedTwice()
    {
        // Arrange (AC-4)
        var sessionId = Guid.NewGuid();
        var documentId = await SeedDocumentAsync(sessionId, Text("Powtarzalne przetwarzanie. " + new string('y', 1500)), "idem.txt", "text/plain");
        await RunProcessingAsync(documentId);
        int firstCount = await InSessionAsync(sessionId, db => db.Chunks.CountAsync(c => c.DocumentId == documentId));

        // Act — process again (redelivery).
        await RunProcessingAsync(documentId);

        // Assert — no duplicates (replace + unique (document_id, index)).
        int secondCount = await InSessionAsync(sessionId, db => db.Chunks.CountAsync(c => c.DocumentId == documentId));
        secondCount.Should().Be(firstCount);
        firstCount.Should().BeGreaterThan(0);
    }
}
