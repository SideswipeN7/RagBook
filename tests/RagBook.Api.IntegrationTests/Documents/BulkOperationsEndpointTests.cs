using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RagBook.Infrastructure.SharedContext.Persistence;
using RagBook.Modules.Documents.Domain;
using RagBook.Modules.Documents.Features.UploadDocument;
using RagBook.Modules.Documents.Processing;
using RagBook.Shared.Sessions;
using Xunit;

namespace RagBook.Api.IntegrationTests.Documents;

/// <summary>
/// Acceptance tests for the bulk endpoints (US-12) against the real host + Dockerized pgvector:
/// <c>POST /api/documents/bulk-move</c> and <c>POST /api/documents/bulk-delete</c>. Proves the happy paths
/// (all moved / all deleted + chunk cascade + quota −N) and the all-or-nothing safety contract (a read-only
/// demo doc, a missing target folder, or another session's id → 422 <c>document.bulk_validation_failed</c> with
/// a per-id <c>failures[]</c> and **nothing changed**).
/// </summary>
public sealed class BulkOperationsEndpointTests(RagBookApiFactory factory) : IClassFixture<RagBookApiFactory>
{
    private static readonly DateTimeOffset SeededAt = new(2026, 7, 16, 12, 0, 0, TimeSpan.Zero);

    private HttpClient CreateClient(Guid sessionId)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = false,
        });
        client.DefaultRequestHeaders.Add("Cookie", $"ragbook_session={sessionId}");

        return client;
    }

    private async Task<Guid> SeedDocumentAsync(Guid sessionId, Guid? folderId, string content = "tresc")
    {
        using IServiceScope scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ISessionInitializer>().Initialize(sessionId);
        var storage = scope.ServiceProvider.GetRequiredService<IFileStorage>();
        var dbContext = scope.ServiceProvider.GetRequiredService<RagBookDbContext>();

        // Unique name per document — the per-folder unique-filename constraint (US-04 dedup) rejects duplicates.
        string fileName = $"doc-{Guid.NewGuid():N}.txt";
        string storagePath = await storage.SaveAsync(
            new MemoryStream(Encoding.UTF8.GetBytes(content)), fileName, CancellationToken.None);
        Document document = Document.CreateUpload(
            content.Length, fileName, "text/plain", folderId, storagePath, SeededAt).Value;
        dbContext.Documents.Add(document);
        await dbContext.SaveChangesAsync();

        return document.Id;
    }

    private async Task<Guid> SeedDemoDocumentAsync(Guid sessionId)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ISessionInitializer>().Initialize(sessionId);
        var dbContext = scope.ServiceProvider.GetRequiredService<RagBookDbContext>();

        Document demo = Document.CreateForQuota(5, DocumentOrigin.Demo).Value;
        dbContext.Documents.Add(demo);
        await dbContext.SaveChangesAsync();

        return demo.Id;
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

    private static async Task<Guid> CreateFolderAsync(HttpClient client, string name)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync("/api/folders", new { name });
        response.EnsureSuccessStatusCode();
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return body.RootElement.GetProperty("id").GetGuid();
    }

    private async Task<T> InSessionAsync<T>(Guid sessionId, Func<RagBookDbContext, Task<T>> query)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ISessionInitializer>().Initialize(sessionId);
        var dbContext = scope.ServiceProvider.GetRequiredService<RagBookDbContext>();

        return await query(dbContext);
    }

    private static async Task<IReadOnlyList<(Guid Id, string Code)>> FailuresOf(HttpResponseMessage response)
    {
        using JsonDocument problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        if (!problem.RootElement.TryGetProperty("failures", out JsonElement failures))
        {
            return [];
        }

        return failures.EnumerateArray()
            .Select(item => (item.GetProperty("id").GetGuid(), item.GetProperty("code").GetString()!))
            .ToList();
    }

    private static async Task<string?> CodeOf(HttpResponseMessage response)
    {
        using JsonDocument problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return problem.RootElement.TryGetProperty("code", out JsonElement value) ? value.GetString() : null;
    }

    [Fact]
    public async Task Should_MoveAllSelected_IntoFolder()
    {
        // Arrange — three documents across different starting places (US-12 AC-2).
        var session = Guid.NewGuid();
        HttpClient client = CreateClient(session);
        Guid source = await CreateFolderAsync(client, "Zrodlo");
        Guid archive = await CreateFolderAsync(client, "Archiwum");
        Guid a = await SeedDocumentAsync(session, folderId: null);
        Guid b = await SeedDocumentAsync(session, folderId: source);
        Guid c = await SeedDocumentAsync(session, folderId: null);

        // Act
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/documents/bulk-move", new { ids = new[] { a, b, c }, targetFolderId = archive });

        // Assert — all three now in Archiwum.
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        List<Guid?> folders = await InSessionAsync(session, db => db.Documents
            .Where(d => d.Id == a || d.Id == b || d.Id == c)
            .Select(d => d.FolderId)
            .ToListAsync());
        folders.Should().OnlyContain(folderId => folderId == archive);
    }

    [Fact]
    public async Task Should_DeleteAllSelected_WithChunkCascade_And_DropQuota()
    {
        // Arrange — three documents, one indexed (has chunks) to prove the cascade (US-12 AC-3).
        var session = Guid.NewGuid();
        HttpClient client = CreateClient(session);
        Guid a = await SeedDocumentAsync(session, folderId: null, content: "Tresc do zaindeksowania. " + new string('x', 1500));
        await RunProcessingAsync(a);
        Guid b = await SeedDocumentAsync(session, folderId: null);
        Guid c = await SeedDocumentAsync(session, folderId: null);
        (await InSessionAsync(session, db => db.Chunks.CountAsync(chunk => chunk.DocumentId == a)))
            .Should().BeGreaterThan(0);
        int before = await InSessionAsync(session, db => db.Documents.CountAsync());

        // Act
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/documents/bulk-delete", new { ids = new[] { a, b, c } });

        // Assert — all gone, chunks cascaded, quota (document count) dropped by exactly 3.
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await InSessionAsync(session, db => db.Documents.CountAsync(d => d.Id == a || d.Id == b || d.Id == c)))
            .Should().Be(0);
        (await InSessionAsync(session, db => db.Chunks.IgnoreQueryFilters().CountAsync(chunk => chunk.DocumentId == a)))
            .Should().Be(0);
        (await InSessionAsync(session, db => db.Documents.CountAsync())).Should().Be(before - 3);
    }

    [Fact]
    public async Task Should_RejectWholeDelete_When_SelectionIncludesReadOnlyDemo()
    {
        // Arrange — two real documents + a read-only demo document (US-12 AC-4).
        var session = Guid.NewGuid();
        HttpClient client = CreateClient(session);
        Guid a = await SeedDocumentAsync(session, folderId: null);
        Guid b = await SeedDocumentAsync(session, folderId: null);
        Guid demo = await SeedDemoDocumentAsync(session);

        // Act
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/documents/bulk-delete", new { ids = new[] { a, b, demo } });

        // Assert — 422, the demo item named as read-only, and NOTHING deleted.
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await CodeOf(response)).Should().Be("document.bulk_validation_failed");
        (await FailuresOf(response)).Should().ContainSingle()
            .Which.Should().Be((demo, "document.read_only"));
        (await InSessionAsync(session, db => db.Documents.CountAsync(d => d.Id == a || d.Id == b || d.Id == demo)))
            .Should().Be(3);
    }

    [Fact]
    public async Task Should_RejectWholeMove_When_TargetFolderMissing()
    {
        // Arrange — a real document but a non-existent target folder (US-12 AC-4).
        var session = Guid.NewGuid();
        HttpClient client = CreateClient(session);
        Guid a = await SeedDocumentAsync(session, folderId: null);
        var missingFolder = Guid.NewGuid();

        // Act
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/documents/bulk-move", new { ids = new[] { a }, targetFolderId = missingFolder });

        // Assert — 422 with the target folder id named, nothing moved (still at root).
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await FailuresOf(response)).Should().ContainSingle()
            .Which.Should().Be((missingFolder, "folder.not_found"));
        (await InSessionAsync(session, db => db.Documents.Where(d => d.Id == a).Select(d => d.FolderId).SingleAsync()))
            .Should().BeNull();
    }

    [Fact]
    public async Task Should_RejectWhole_And_ReportNotFound_When_SelectionIncludesAnotherSessionsId()
    {
        // Arrange — session A owns one document; session B owns two and tries to bulk-delete A's id too (AC-5).
        var sessionA = Guid.NewGuid();
        var sessionB = Guid.NewGuid();
        Guid foreignDoc = await SeedDocumentAsync(sessionA, folderId: null);
        HttpClient clientB = CreateClient(sessionB);
        Guid b1 = await SeedDocumentAsync(sessionB, folderId: null);
        Guid b2 = await SeedDocumentAsync(sessionB, folderId: null);

        // Act
        HttpResponseMessage response = await clientB.PostAsJsonAsync(
            "/api/documents/bulk-delete", new { ids = new[] { b1, b2, foreignDoc } });

        // Assert — the foreign id reads as not-found (no disclosure); nothing deleted in either session.
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await FailuresOf(response)).Should().ContainSingle()
            .Which.Should().Be((foreignDoc, "document.not_found"));
        (await InSessionAsync(sessionB, db => db.Documents.CountAsync(d => d.Id == b1 || d.Id == b2))).Should().Be(2);
        (await InSessionAsync(sessionA, db => db.Documents.AnyAsync(d => d.Id == foreignDoc))).Should().BeTrue();
    }

    [Fact]
    public async Task Should_Return400_When_ListEmpty()
    {
        var session = Guid.NewGuid();
        HttpClient client = CreateClient(session);

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/documents/bulk-delete", new { ids = Array.Empty<Guid>() });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeOf(response)).Should().Be("document.bulk_empty");
    }
}
