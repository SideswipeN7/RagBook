using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RagBook.Infrastructure.SharedContext.Persistence;
using RagBook.Modules.Documents.Domain;
using RagBook.Modules.Documents.Features.UploadDocument;
using RagBook.Modules.Documents.Processing;
using RagBook.Shared.Sessions;
using Xunit;

namespace RagBook.Api.IntegrationTests.Documents;

/// <summary>
/// Acceptance tests for <c>DELETE /api/documents/{id}</c> against the real host + Dockerized pgvector.
/// Documents are seeded directly (blob + row, without publishing the upload event) so processing only
/// happens when a test invokes the handler explicitly.
/// </summary>
public sealed class DeleteDocumentEndpointTests(RagBookApiFactory factory) : IClassFixture<RagBookApiFactory>
{
    private static readonly DateTimeOffset SeededAt = new(2026, 7, 11, 12, 0, 0, TimeSpan.Zero);

    private static byte[] Text(string content) => Encoding.UTF8.GetBytes(content);

    private async Task<Guid> SeedDocumentAsync(WebApplicationFactory<Program> host, Guid sessionId, string content)
    {
        using IServiceScope scope = host.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ISessionInitializer>().Initialize(sessionId);
        var storage = scope.ServiceProvider.GetRequiredService<IFileStorage>();
        var dbContext = scope.ServiceProvider.GetRequiredService<RagBookDbContext>();

        string storagePath = await storage.SaveAsync(new MemoryStream(Text(content)), "doc.txt", CancellationToken.None);
        Document document = Document.CreateUpload(content.Length, "doc.txt", "text/plain", null, storagePath, SeededAt).Value;
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

    private static async Task<(HttpStatusCode Status, string? Code)> DeleteAsync(
        WebApplicationFactory<Program> host,
        Guid sessionId,
        Guid documentId)
    {
        var client = host.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = false,
        });
        client.DefaultRequestHeaders.Add("Cookie", $"ragbook_session={sessionId}");

        HttpResponseMessage response = await client.DeleteAsync($"/api/documents/{documentId}");
        if (response.StatusCode == HttpStatusCode.NoContent)
        {
            return (response.StatusCode, null);
        }

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        string? code = document.RootElement.TryGetProperty("code", out JsonElement value) ? value.GetString() : null;

        return (response.StatusCode, code);
    }

    [Fact]
    public async Task Should_DeletePresentDocument()
    {
        // Arrange (AC-1)
        var sessionId = Guid.NewGuid();
        var documentId = await SeedDocumentAsync(factory, sessionId, "Do usuniecia.");

        // Act
        var delete = await DeleteAsync(factory, sessionId, documentId);

        // Assert
        delete.Status.Should().Be(HttpStatusCode.NoContent);
        (await InSessionAsync(sessionId, db => db.Documents.AnyAsync(d => d.Id == documentId))).Should().BeFalse();
    }

    [Fact]
    public async Task Should_CascadeDeleteChunks_When_DocumentDeleted()
    {
        // Arrange (AC-2) — index a document so it has chunks.
        var sessionId = Guid.NewGuid();
        var documentId = await SeedDocumentAsync(factory, sessionId, "Tresc do zaindeksowania. " + new string('x', 1500));
        await RunProcessingAsync(documentId);
        (await InSessionAsync(sessionId, db => db.Chunks.CountAsync(c => c.DocumentId == documentId))).Should().BeGreaterThan(0);

        // Act
        (await DeleteAsync(factory, sessionId, documentId)).Status.Should().Be(HttpStatusCode.NoContent);

        // Assert — the index cascaded away with the document.
        (await InSessionAsync(sessionId, db => db.Chunks.IgnoreQueryFilters().CountAsync(c => c.DocumentId == documentId)))
            .Should().Be(0);
    }

    [Fact]
    public async Task Should_Delete_And_WorkerAbortsQuietly_When_ProcessingDocumentDeleted()
    {
        // Arrange (AC-3) — a processing document (not yet indexed).
        var sessionId = Guid.NewGuid();
        var documentId = await SeedDocumentAsync(factory, sessionId, "Przetwarzany dokument.");

        // Act — delete it, then run processing (worker must abort quietly).
        (await DeleteAsync(factory, sessionId, documentId)).Status.Should().Be(HttpStatusCode.NoContent);
        await RunProcessingAsync(documentId); // must not throw

        // Assert — no chunks written for the deleted document.
        (await InSessionAsync(sessionId, db => db.Chunks.IgnoreQueryFilters().CountAsync(c => c.DocumentId == documentId)))
            .Should().Be(0);
    }

    [Fact]
    public async Task Should_Return404_When_DeletingAnotherSessionsDocument()
    {
        // Arrange (AC-4)
        var owner = Guid.NewGuid();
        var intruder = Guid.NewGuid();
        var documentId = await SeedDocumentAsync(factory, owner, "Cudzy dokument.");

        // Act
        var delete = await DeleteAsync(factory, intruder, documentId);

        // Assert — 404, and the owner's document is untouched.
        delete.Status.Should().Be(HttpStatusCode.NotFound);
        delete.Code.Should().Be("document.not_found");
        (await InSessionAsync(owner, db => db.Documents.AnyAsync(d => d.Id == documentId))).Should().BeTrue();
    }

    [Fact]
    public async Task Should_Return404_When_DeletingTwice()
    {
        // Arrange (AC-5 / idempotent-from-user)
        var sessionId = Guid.NewGuid();
        var documentId = await SeedDocumentAsync(factory, sessionId, "Raz.");

        // Act
        (await DeleteAsync(factory, sessionId, documentId)).Status.Should().Be(HttpStatusCode.NoContent);
        var second = await DeleteAsync(factory, sessionId, documentId);

        // Assert
        second.Status.Should().Be(HttpStatusCode.NotFound);
        second.Code.Should().Be("document.not_found");
    }

    [Fact]
    public async Task Should_StillDelete_When_BlobRemovalFails()
    {
        // Arrange (FR-004) — a real seeded document, then delete it through a repository whose IFileStorage
        // throws on delete (LocalFileStorage no-ops on a missing file, so a bogus path would not fail). The
        // repository is exercised directly, so the best-effort tolerance is what's under test.
        var sessionId = Guid.NewGuid();
        var documentId = await SeedDocumentAsync(factory, sessionId, "Blob delete zawiedzie.");

        bool deleted;
        using (IServiceScope scope = factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<ISessionInitializer>().Initialize(sessionId);
            var repository = new DocumentDeletionRepository(
                scope.ServiceProvider.GetRequiredService<RagBookDbContext>(),
                new ThrowingOnDeleteFileStorage(),
                NullLogger<DocumentDeletionRepository>.Instance);

            // Act — must not throw despite the storage failure.
            deleted = await repository.DeleteAsync(documentId, CancellationToken.None);
        }

        // Assert — deleted true; the record is gone (orphaned blob tolerated + logged).
        deleted.Should().BeTrue();
        (await InSessionAsync(sessionId, db => db.Documents.AnyAsync(d => d.Id == documentId))).Should().BeFalse();
    }

    private sealed class ThrowingOnDeleteFileStorage : IFileStorage
    {
        public Task<string> SaveAsync(Stream content, string suggestedName, CancellationToken cancellationToken)
            => Task.FromResult($"stub/{Guid.NewGuid():N}.txt");

        public Task<Stream> OpenReadAsync(string storagePath, CancellationToken cancellationToken)
            => Task.FromResult<Stream>(new MemoryStream());

        public Task DeleteAsync(string storagePath, CancellationToken cancellationToken)
            => throw new IOException("storage unavailable");
    }
}
