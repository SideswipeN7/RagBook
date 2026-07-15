using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RagBook.Api.IntegrationTests.Chat;
using RagBook.Infrastructure.SharedContext.Persistence;
using RagBook.Modules.Documents.Domain;
using RagBook.Shared.Sessions;
using Xunit;

namespace RagBook.Api.IntegrationTests.Documents;

/// <summary>
/// Acceptance tests for <c>PATCH /api/documents/{id}/folder</c> (US-10) against the real host + Dockerized
/// pgvector. Moves a document to a folder / the root, guards ownership + target folder, no-ops the same folder,
/// keeps chunks untouched, and enforces cross-session isolation. (The read-only demo guard is covered at the
/// Application tier — a persistable demo document arrives with US-03.)
/// </summary>
public sealed class MoveDocumentEndpointTests(RagBookApiFactory factory) : IClassFixture<RagBookApiFactory>
{
    private static readonly DateTimeOffset SeededAt = new(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions JsonWeb = new(JsonSerializerDefaults.Web);

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

    private async Task<Guid> SeedDocumentAsync(Guid sessionId, Guid? folderId)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ISessionInitializer>().Initialize(sessionId);
        var storage = scope.ServiceProvider.GetRequiredService<IFileStorage>();
        var dbContext = scope.ServiceProvider.GetRequiredService<RagBookDbContext>();

        string storagePath = await storage.SaveAsync(new MemoryStream(Encoding.UTF8.GetBytes("tresc")), "doc.txt", CancellationToken.None);
        Document document = Document.CreateUpload(5, "doc.txt", "text/plain", folderId, storagePath, SeededAt).Value;
        dbContext.Documents.Add(document);
        await dbContext.SaveChangesAsync();

        return document.Id;
    }

    private static async Task<Guid> CreateFolderAsync(HttpClient client, string name)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync("/api/folders", new { name });
        response.EnsureSuccessStatusCode();
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return body.RootElement.GetProperty("id").GetGuid();
    }

    private async Task<Guid?> FolderOfAsync(Guid sessionId, Guid documentId)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ISessionInitializer>().Initialize(sessionId);
        var dbContext = scope.ServiceProvider.GetRequiredService<RagBookDbContext>();

        return await dbContext.Documents.Where(document => document.Id == documentId).Select(document => document.FolderId).SingleAsync();
    }

    private static async Task<string?> CodeOf(HttpResponseMessage response)
    {
        using JsonDocument problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return problem.RootElement.TryGetProperty("code", out JsonElement value) ? value.GetString() : null;
    }

    [Fact]
    public async Task Should_MoveDocumentToFolder_And_PersistIt()
    {
        // Arrange — a root (Processing) document and a folder (FR-007: a Processing document is movable).
        var session = Guid.NewGuid();
        HttpClient client = CreateClient(session);
        Guid document = await SeedDocumentAsync(session, folderId: null);
        Guid folder = await CreateFolderAsync(client, "Umowy");

        // Act
        HttpResponseMessage response = await client.PatchAsJsonAsync($"/api/documents/{document}/folder", new { folderId = folder });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await FolderOfAsync(session, document)).Should().Be(folder);
    }

    [Fact]
    public async Task Should_MoveDocumentToRoot_When_FolderIdNull()
    {
        // Arrange — a document inside a folder.
        var session = Guid.NewGuid();
        HttpClient client = CreateClient(session);
        Guid folder = await CreateFolderAsync(client, "Umowy");
        Guid document = await SeedDocumentAsync(session, folder);

        // Act
        HttpResponseMessage response = await client.PatchAsJsonAsync($"/api/documents/{document}/folder", new { folderId = (Guid?)null });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await FolderOfAsync(session, document)).Should().BeNull();
    }

    [Fact]
    public async Task Should_Return404_When_DocumentNotFound()
    {
        var session = Guid.NewGuid();
        HttpClient client = CreateClient(session);

        HttpResponseMessage response = await client.PatchAsJsonAsync($"/api/documents/{Guid.NewGuid()}/folder", new { folderId = (Guid?)null });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await CodeOf(response)).Should().Be("document.not_found");
    }

    [Fact]
    public async Task Should_Return404_When_TargetFolderNotFound()
    {
        var session = Guid.NewGuid();
        HttpClient client = CreateClient(session);
        Guid document = await SeedDocumentAsync(session, folderId: null);

        HttpResponseMessage response = await client.PatchAsJsonAsync($"/api/documents/{document}/folder", new { folderId = Guid.NewGuid() });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await CodeOf(response)).Should().Be("folder.not_found");
    }

    [Fact]
    public async Task Should_NoOp_When_AlreadyInTargetFolder()
    {
        var session = Guid.NewGuid();
        HttpClient client = CreateClient(session);
        Guid folder = await CreateFolderAsync(client, "Umowy");
        Guid document = await SeedDocumentAsync(session, folder);

        HttpResponseMessage response = await client.PatchAsJsonAsync($"/api/documents/{document}/folder", new { folderId = folder });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await FolderOfAsync(session, document)).Should().Be(folder);
    }

    [Fact]
    public async Task Should_KeepChunksUntouched_When_Moved()
    {
        // Arrange — a ready document with a chunk (US-16 seed). The move must not re-index (SC-003).
        var session = Guid.NewGuid();
        HttpClient client = CreateClient(session);
        Guid document = await ChatRetrievalSeed.SeedReadyDocumentAsync(factory, session, "umowa.pdf", null, [("okres wypowiedzenia", 1)]);
        Guid folder = await CreateFolderAsync(client, "Umowy");
        int chunksBefore = await ChunkCountAsync(session, document);

        // Act
        HttpResponseMessage response = await client.PatchAsJsonAsync($"/api/documents/{document}/folder", new { folderId = folder });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await ChunkCountAsync(session, document)).Should().Be(chunksBefore).And.BeGreaterThan(0);
    }

    [Fact]
    public async Task Should_Return404_When_MovingAnotherSessionsDocument()
    {
        // Arrange — session A owns the document.
        var sessionA = Guid.NewGuid();
        var sessionB = Guid.NewGuid();
        Guid document = await SeedDocumentAsync(sessionA, folderId: null);
        HttpClient clientB = CreateClient(sessionB);

        // Act
        HttpResponseMessage response = await clientB.PatchAsJsonAsync($"/api/documents/{document}/folder", new { folderId = (Guid?)null });

        // Assert — invisible to session B → not found, never disclosing existence.
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await CodeOf(response)).Should().Be("document.not_found");
    }

    private async Task<int> ChunkCountAsync(Guid sessionId, Guid documentId)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ISessionInitializer>().Initialize(sessionId);
        var dbContext = scope.ServiceProvider.GetRequiredService<RagBookDbContext>();

        return await dbContext.Chunks.CountAsync(chunk => chunk.DocumentId == documentId);
    }
}
