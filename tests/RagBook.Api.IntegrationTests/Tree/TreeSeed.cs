using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RagBook.Infrastructure.SharedContext.Persistence;
using RagBook.Modules.Documents.Domain;
using RagBook.Shared.Sessions;

namespace RagBook.Api.IntegrationTests.Tree;

/// <summary>
/// Seeds documents that the upload API cannot produce — a demo-origin document (excluded from the tree)
/// and a failed document with a recorded reason (US-06's future write, needed to prove the US-07 read
/// surfaces <c>failure_reason</c>). Uses the persistence layer directly with the session established via
/// <see cref="ISessionInitializer"/>, so the stamping interceptor and query filter behave as for a request.
/// </summary>
internal static class TreeSeed
{
    private static readonly DateTimeOffset SeededAt = new(2026, 7, 11, 9, 0, 0, TimeSpan.Zero);

    /// <summary>Seeds a demo-origin document (should be excluded from the session tree).</summary>
    public static async Task SeedDemoDocumentAsync(RagBookApiFactory factory, Guid sessionId)
    {
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ISessionInitializer>().Initialize(sessionId);
        var dbContext = scope.ServiceProvider.GetRequiredService<RagBookDbContext>();

        dbContext.Documents.Add(Document.CreateForQuota(1_000, DocumentOrigin.Demo).Value);
        await dbContext.SaveChangesAsync();
    }

    /// <summary>Seeds a failed user document carrying <paramref name="reason"/> (stands in for US-06).</summary>
    public static async Task SeedFailedDocumentAsync(RagBookApiFactory factory, Guid sessionId, string fileName, string reason)
    {
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ISessionInitializer>().Initialize(sessionId);
        var dbContext = scope.ServiceProvider.GetRequiredService<RagBookDbContext>();

        Document document = Document.CreateUpload(2_000, fileName, "application/pdf", folderId: null, "seed/blob.pdf", SeededAt).Value;
        var entry = dbContext.Documents.Add(document);

        // Status and FailureReason have private setters (US-06 owns the transition); set through EF's
        // metadata so the test can seed a Failed document with a reason.
        entry.Property(candidate => candidate.Status).CurrentValue = DocumentStatus.Failed;
        entry.Property(candidate => candidate.FailureReason).CurrentValue = reason;

        await dbContext.SaveChangesAsync();
    }
}
