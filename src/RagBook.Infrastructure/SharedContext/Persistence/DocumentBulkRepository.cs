using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RagBook.Modules.Documents.Domain;

namespace RagBook.Infrastructure.SharedContext.Persistence;

/// <summary>
/// EF Core implementation of <see cref="IDocumentBulkRepository"/> (US-12). Reads flow through the global session
/// query filter, so a cross-session / unknown id is simply absent from <see cref="GetByIdsAsync"/> (→ reported as
/// not-found, §III). <see cref="MoveAllAsync"/> persists every folder change in one <c>SaveChanges</c>;
/// <see cref="DeleteAllAsync"/> removes every row in a single transaction — the <c>chunks</c> FK cascades the
/// index away (US-06) — then makes a **best-effort** removal of each stored binary (a storage failure is logged
/// and swallowed; an orphaned blob is the accepted trade-off, reusing the US-08 pattern).
/// </summary>
public sealed class DocumentBulkRepository(
    RagBookDbContext dbContext,
    IFileStorage fileStorage,
    ILogger<DocumentBulkRepository> logger)
    : IDocumentBulkRepository
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<Document>> GetByIdsAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken)
    {
        return await dbContext.Documents
            .Where(document => ids.Contains(document.Id))
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task MoveAllAsync(
        IReadOnlyList<Document> documents,
        Guid? targetFolderId,
        CancellationToken cancellationToken)
    {
        foreach (Document document in documents)
        {
            document.MoveToFolder(targetFolderId);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task DeleteAllAsync(IReadOnlyList<Document> documents, CancellationToken cancellationToken)
    {
        // Capture the blob pointers before the rows are gone; the DB delete is the source of truth.
        var storagePaths = documents
            .Select(document => (document.Id, document.StoragePath))
            .ToList();

        await using (var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken))
        {
            dbContext.Documents.RemoveRange(documents);
            await dbContext.SaveChangesAsync(cancellationToken); // the chunks FK cascades the index away
            await transaction.CommitAsync(cancellationToken);
        }

        foreach ((Guid documentId, string? storagePath) in storagePaths)
        {
            if (storagePath is null)
            {
                continue;
            }

            try
            {
                await fileStorage.DeleteAsync(storagePath, cancellationToken);
            }
            catch (Exception exception)
            {
                // Best-effort: the records and index are already gone; tolerate an orphaned blob (US-08 pattern).
                logger.LogWarning(
                    exception,
                    "Failed to delete blob {StoragePath} for document {DocumentId}; leaving an orphaned file.",
                    storagePath,
                    documentId);
            }
        }
    }
}
