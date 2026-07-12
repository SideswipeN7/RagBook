using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using RagBook.Infrastructure.SharedContext.Persistence;
using RagBook.Modules.Documents.Domain;
using RagBook.Modules.Folders.Domain;
using RagBook.Shared.Sessions;

namespace RagBook.Api.IntegrationTests.Chat;

/// <summary>
/// Seeds the scoped-retrieval fixtures US-13 tests need: folders (materialized path via the domain),
/// **Ready** documents (status set through EF metadata, like <c>TreeSeed</c>), and their chunks — inserted
/// through the real <c>IChunkRepository</c> with embeddings from the session's <see cref="IEmbeddingProvider"/>
/// (the deterministic fake), so a query over a known text is comparable. All writes go through an
/// initialized session so the stamping interceptor + filters behave as for a request.
/// </summary>
internal static class ChatRetrievalSeed
{
    private static readonly FolderNameRules Rules = new(100);
    private const int MaxDepth = 3;
    private static readonly DateTimeOffset SeededAt = new(2026, 7, 12, 9, 0, 0, TimeSpan.Zero);

    /// <summary>Seeds a root folder and returns it (its <c>Path</c> is the scope path for the subtree).</summary>
    public static async Task<Folder> SeedRootFolderAsync(WebApplicationFactory<Program> factory, Guid sessionId, string name)
    {
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ISessionInitializer>().Initialize(sessionId);
        var dbContext = scope.ServiceProvider.GetRequiredService<RagBookDbContext>();

        Folder folder = Folder.CreateRoot(name, Rules).Value;
        dbContext.Folders.Add(folder);
        await dbContext.SaveChangesAsync();

        return folder;
    }

    /// <summary>Seeds a child of <paramref name="parent"/> and returns it.</summary>
    public static async Task<Folder> SeedChildFolderAsync(WebApplicationFactory<Program> factory, Guid sessionId, Folder parent, string name)
    {
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ISessionInitializer>().Initialize(sessionId);
        var dbContext = scope.ServiceProvider.GetRequiredService<RagBookDbContext>();

        Folder folder = Folder.CreateChild(parent, name, Rules, MaxDepth).Value;
        dbContext.Folders.Add(folder);
        await dbContext.SaveChangesAsync();

        return folder;
    }

    /// <summary>Seeds a Ready document in <paramref name="folderId"/> with the given chunk texts/pages; returns its id.</summary>
    public static Task<Guid> SeedReadyDocumentAsync(
        WebApplicationFactory<Program> factory,
        Guid sessionId,
        string fileName,
        Guid? folderId,
        (string Text, int? Page)[] chunks)
    {
        return SeedDocumentWithChunksAsync(factory, sessionId, fileName, folderId, DocumentStatus.Ready, chunks);
    }

    /// <summary>Seeds a document that HAS chunks but is <paramref name="status"/> (e.g. Failed) — to prove ready-only filtering.</summary>
    public static async Task<Guid> SeedDocumentWithChunksAsync(
        WebApplicationFactory<Program> factory,
        Guid sessionId,
        string fileName,
        Guid? folderId,
        DocumentStatus status,
        (string Text, int? Page)[] chunks)
    {
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ISessionInitializer>().Initialize(sessionId);
        var dbContext = scope.ServiceProvider.GetRequiredService<RagBookDbContext>();
        var embeddingProvider = scope.ServiceProvider.GetRequiredService<IEmbeddingProvider>();
        var chunkRepository = scope.ServiceProvider.GetRequiredService<IChunkRepository>();

        Document document = Document.CreateUpload(1_000, fileName, "application/pdf", folderId, "seed/blob.pdf", SeededAt).Value;
        var entry = dbContext.Documents.Add(document);
        entry.Property(candidate => candidate.Status).CurrentValue = status;
        await dbContext.SaveChangesAsync();

        IReadOnlyList<string> texts = chunks.Select(chunk => chunk.Text).ToList();
        IReadOnlyList<float[]> vectors = await embeddingProvider.EmbedBatchAsync(texts, CancellationToken.None);
        List<Chunk> chunkEntities = chunks
            .Select((chunk, index) => Chunk.Create(document.Id, index, chunk.Text, chunk.Page, vectors[index]))
            .ToList();
        await chunkRepository.ReplaceForDocumentAsync(document, chunkEntities, CancellationToken.None);

        return document.Id;
    }

    /// <summary>Seeds a still-processing document (no chunks) in <paramref name="folderId"/>; returns its id.</summary>
    public static async Task<Guid> SeedProcessingDocumentAsync(WebApplicationFactory<Program> factory, Guid sessionId, string fileName, Guid? folderId)
    {
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ISessionInitializer>().Initialize(sessionId);
        var dbContext = scope.ServiceProvider.GetRequiredService<RagBookDbContext>();

        Document document = Document.CreateUpload(1_000, fileName, "application/pdf", folderId, "seed/blob.pdf", SeededAt).Value;
        dbContext.Documents.Add(document); // stays Processing (default)
        await dbContext.SaveChangesAsync();

        return document.Id;
    }
}
