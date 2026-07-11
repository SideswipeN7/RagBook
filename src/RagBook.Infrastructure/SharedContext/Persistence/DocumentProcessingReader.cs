using Microsoft.EntityFrameworkCore;
using RagBook.Modules.Documents.Domain;

namespace RagBook.Infrastructure.SharedContext.Persistence;

/// <summary>
/// EF Core implementation of <see cref="IDocumentProcessingReader"/> (US-06). <see cref="GetTargetAsync"/>
/// reads **with the session query filter bypassed** (by id only) so the background worker can discover the
/// owning session before it has one; <see cref="LoadTrackedAsync"/> is a normal session-scoped tracked
/// load, used after the handler has initialized the ambient session, so the status transition persists.
/// </summary>
public sealed class DocumentProcessingReader(RagBookDbContext dbContext) : IDocumentProcessingReader
{
    /// <inheritdoc />
    public async Task<ProcessingTarget?> GetTargetAsync(Guid documentId, CancellationToken cancellationToken)
    {
        var row = await dbContext.Documents
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(document => document.Id == documentId)
            .Select(document => new { document.UserSessionId, document.StoragePath, document.ContentType })
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null || row.StoragePath is null || row.ContentType is null)
        {
            return null;
        }

        return new ProcessingTarget(row.UserSessionId, row.StoragePath, row.ContentType);
    }

    /// <inheritdoc />
    public Task<Document?> LoadTrackedAsync(Guid documentId, CancellationToken cancellationToken)
    {
        return dbContext.Documents.FirstOrDefaultAsync(document => document.Id == documentId, cancellationToken);
    }
}
