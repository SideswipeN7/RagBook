using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RagBook.Infrastructure.SharedContext.Persistence;
using RagBook.Modules.Documents.Domain;
using RagBook.Modules.Folders.Domain;
using RagBook.Shared.Sessions;
using Xunit;

namespace RagBook.Api.IntegrationTests.Folders;

/// <summary>
/// Acceptance tests for <c>PATCH /api/folders/{id}/parent</c> (US-11) against the real host + Dockerized pgvector.
/// Moves a folder with its subtree (rewriting every descendant's path), to the root, and guards cycle / depth /
/// duplicate-name / cross-session isolation; documents are untouched and remain within the new ancestor's subtree.
/// </summary>
public sealed class MoveFolderEndpointTests(RagBookApiFactory factory) : IClassFixture<RagBookApiFactory>
{
    private static readonly DateTimeOffset SeededAt = new(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);

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

    private static async Task<Guid> CreateFolderAsync(HttpClient client, string name, Guid? parentId)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync("/api/folders", new { name, parentId });
        response.EnsureSuccessStatusCode();
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return body.RootElement.GetProperty("id").GetGuid();
    }

    private static Task<HttpResponseMessage> MoveAsync(HttpClient client, Guid folderId, Guid? parentId)
    {
        return client.PatchAsJsonAsync($"/api/folders/{folderId}/parent", new { parentId });
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

    private async Task<(Guid? ParentId, string Path)> FolderRowAsync(Guid sessionId, Guid folderId)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ISessionInitializer>().Initialize(sessionId);
        var dbContext = scope.ServiceProvider.GetRequiredService<RagBookDbContext>();
        Folder folder = await dbContext.Folders.AsNoTracking().SingleAsync(candidate => candidate.Id == folderId);

        return (folder.ParentId, folder.Path);
    }

    private async Task<Guid?> DocumentFolderAsync(Guid sessionId, Guid documentId)
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
    public async Task Should_MoveFolderWithSubtree_RewriteDescendantPaths_AndKeepDocuments()
    {
        // Arrange — Umowy/2026 (a document inside 2026) and a folder Archiwum.
        var session = Guid.NewGuid();
        HttpClient client = CreateClient(session);
        Guid umowy = await CreateFolderAsync(client, "Umowy", null);
        Guid rok = await CreateFolderAsync(client, "2026", umowy);
        Guid archiwum = await CreateFolderAsync(client, "Archiwum", null);
        Guid document = await SeedDocumentAsync(session, rok);

        // Act — move Umowy into Archiwum.
        HttpResponseMessage response = await MoveAsync(client, umowy, archiwum);

        // Assert — the subtree is re-parented and every descendant's path is rewritten under Archiwum.
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (Guid? umowyParent, string umowyPath) = await FolderRowAsync(session, umowy);
        (_, string archiwumPath) = await FolderRowAsync(session, archiwum);
        (_, string rokPath) = await FolderRowAsync(session, rok);

        umowyParent.Should().Be(archiwum);
        umowyPath.Should().StartWith(archiwumPath); // Archiwum/Umowy/
        rokPath.Should().StartWith(umowyPath); // Archiwum/Umowy/2026/

        // The document is untouched (still in 2026) but now lives within Archiwum's subtree (chat scope follows — SC-007).
        (await DocumentFolderAsync(session, document)).Should().Be(rok);
        rokPath.Should().StartWith(archiwumPath);
    }

    [Fact]
    public async Task Should_MoveFolderToRoot_When_ParentIdNull()
    {
        var session = Guid.NewGuid();
        HttpClient client = CreateClient(session);
        Guid archiwum = await CreateFolderAsync(client, "Archiwum", null);
        Guid umowy = await CreateFolderAsync(client, "Umowy", archiwum);

        HttpResponseMessage response = await MoveAsync(client, umowy, null);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (Guid? parent, string path) = await FolderRowAsync(session, umowy);
        parent.Should().BeNull();
        path.Should().Be($"/{umowy:N}/");
    }

    [Fact]
    public async Task Should_Return409_When_TargetIsADescendant()
    {
        var session = Guid.NewGuid();
        HttpClient client = CreateClient(session);
        Guid a = await CreateFolderAsync(client, "A", null);
        Guid b = await CreateFolderAsync(client, "B", a);

        HttpResponseMessage response = await MoveAsync(client, a, b);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeOf(response)).Should().Be("folder.circular_move");
    }

    [Fact]
    public async Task Should_Return400_When_ResultExceedsMaxDepth()
    {
        // Arrange — R/M/D (depth 3) and T/X (depth 2). Moving M under X ⇒ D would be depth 4.
        var session = Guid.NewGuid();
        HttpClient client = CreateClient(session);
        Guid r = await CreateFolderAsync(client, "R", null);
        Guid m = await CreateFolderAsync(client, "M", r);
        await CreateFolderAsync(client, "D", m);
        Guid t = await CreateFolderAsync(client, "T", null);
        Guid x = await CreateFolderAsync(client, "X", t);

        HttpResponseMessage response = await MoveAsync(client, m, x);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeOf(response)).Should().Be("folder.max_depth_exceeded");
    }

    [Fact]
    public async Task Should_Return409_When_TargetHasSameNamedFolder()
    {
        // Arrange — Archiwum already contains "Umowy"; a separate root "Umowy" tries to move in.
        var session = Guid.NewGuid();
        HttpClient client = CreateClient(session);
        Guid archiwum = await CreateFolderAsync(client, "Archiwum", null);
        await CreateFolderAsync(client, "Umowy", archiwum);
        Guid rootUmowy = await CreateFolderAsync(client, "Umowy", null);

        HttpResponseMessage response = await MoveAsync(client, rootUmowy, archiwum);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeOf(response)).Should().Be("folder.duplicate_name");
    }

    [Fact]
    public async Task Should_Return404_When_MovingAnotherSessionsFolder()
    {
        var sessionA = Guid.NewGuid();
        var sessionB = Guid.NewGuid();
        HttpClient clientA = CreateClient(sessionA);
        Guid folder = await CreateFolderAsync(clientA, "A", null);
        HttpClient clientB = CreateClient(sessionB);

        HttpResponseMessage response = await MoveAsync(clientB, folder, null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await CodeOf(response)).Should().Be("folder.not_found");
    }
}
