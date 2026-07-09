using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RagBook.Infrastructure.SharedContext.Persistence;
using RagBook.Modules.Documents.Domain;
using RagBook.Shared.Sessions;

namespace RagBook.Api.IntegrationTests.Quota;

/// <summary>
/// Seeds session-owned documents directly through the persistence layer for quota tests. The owning
/// session is established via <see cref="ISessionInitializer"/> so the central stamping interceptor
/// and the global query filter behave exactly as they do for a real request.
/// </summary>
internal static class QuotaSeed
{
    /// <summary>A document to seed: its size, origin, and lifecycle status.</summary>
    internal readonly record struct Doc(long SizeBytes, DocumentOrigin Origin, DocumentStatus Status)
    {
        /// <summary>A processing user upload of the given size (the common case).</summary>
        public static Doc User(long sizeBytes)
        {
            return new Doc(sizeBytes, DocumentOrigin.User, DocumentStatus.Processing);
        }
    }

    /// <summary>Persists <paramref name="documents"/> for <paramref name="sessionId"/>.</summary>
    public static async Task SeedAsync(RagBookApiFactory factory, Guid sessionId, params Doc[] documents)
    {
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ISessionInitializer>().Initialize(sessionId);
        var dbContext = scope.ServiceProvider.GetRequiredService<RagBookDbContext>();

        foreach (Doc document in documents)
        {
            Document entity = Document.CreateForQuota(document.SizeBytes, document.Origin).Value;
            var entry = dbContext.Documents.Add(entity);

            // Status has a private setter (US-06 owns the transitions); set it through EF's metadata so
            // tests can seed a Failed document without a public mutator.
            entry.Property(candidate => candidate.Status).CurrentValue = document.Status;
        }

        await dbContext.SaveChangesAsync();
    }

    /// <summary>Removes one of the session's documents (stands in for US-08 delete in AC-4).</summary>
    public static async Task RemoveOneAsync(RagBookApiFactory factory, Guid sessionId)
    {
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ISessionInitializer>().Initialize(sessionId);
        var dbContext = scope.ServiceProvider.GetRequiredService<RagBookDbContext>();

        Document one = await dbContext.Documents.FirstAsync();
        dbContext.Documents.Remove(one);
        await dbContext.SaveChangesAsync();
    }

    /// <summary>Counts the session's quota-counting documents (excludes demo) for assertions.</summary>
    public static async Task<int> CountAsync(RagBookApiFactory factory, Guid sessionId)
    {
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ISessionInitializer>().Initialize(sessionId);
        var dbContext = scope.ServiceProvider.GetRequiredService<RagBookDbContext>();

        return await dbContext.Documents.CountAsync(document => document.Origin != DocumentOrigin.Demo);
    }
}
