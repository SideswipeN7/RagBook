using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RagBook.Modules.Documents.Domain;

namespace RagBook.Infrastructure.SharedContext.Persistence;

/// <summary>
/// EF Core implementation of <see cref="IDocumentDeletionRepository"/> (US-08). Session-scoped: the
/// document is loaded through the global query filter, so a cross-session / already-deleted / unknown id
/// reads as <c>null</c> → <c>false</c> (not found). On a hit it deletes the row inside a transaction — the
/// <c>chunks</c> FK removes the index by cascade (US-06) — commits, then makes a **best-effort** removal of
/// the stored binary: a storage failure is **logged and swallowed** (an orphaned blob is the accepted
/// trade-off, FR-004), never failing the delete.
/// </summary>
public sealed class DocumentDeletionRepository(
    RagBookDbContext dbContext,
    IFileStorage fileStorage,
    ILogger<DocumentDeletionRepository> logger)
    : IDocumentDeletionRepository
{
    /// <inheritdoc />
    public async Task<bool> DeleteAsync(Guid documentId, CancellationToken cancellationToken)
    {
        Document? document = await dbContext.Documents
            .FirstOrDefaultAsync(candidate => candidate.Id == documentId, cancellationToken);
        if (document is null)
        {
            return false; // cross-session / already-deleted / unknown → not found (404)
        }

        string? storagePath = document.StoragePath;

        await using (var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken))
        {
            dbContext.Documents.Remove(document);
            await dbContext.SaveChangesAsync(cancellationToken); // the chunks FK cascades the index away
            await transaction.CommitAsync(cancellationToken);
        }

        if (storagePath is not null)
        {
            try
            {
                await fileStorage.DeleteAsync(storagePath, cancellationToken);
            }
            catch (Exception exception)
            {
                // Best-effort: the record and index are already gone; tolerate an orphaned blob (FR-004).
                logger.LogWarning(
                    exception,
                    "Failed to delete blob {StoragePath} for document {DocumentId}; leaving an orphaned file.",
                    storagePath,
                    documentId);
            }
        }

        return true;
    }
}
