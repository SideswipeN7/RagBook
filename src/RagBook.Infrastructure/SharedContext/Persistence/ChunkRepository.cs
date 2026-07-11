using System.Globalization;
using Microsoft.EntityFrameworkCore;
using RagBook.Modules.Documents.Domain;
using RagBook.Shared.Sessions;

namespace RagBook.Infrastructure.SharedContext.Persistence;

/// <summary>
/// EF Core implementation of <see cref="IChunkRepository"/> (US-06). Chunks are written in a transaction
/// together with the tracked document's status transition: <see cref="ReplaceForDocumentAsync"/> deletes
/// the document's existing chunks then inserts the new set (idempotent — a re-run yields the same set);
/// <see cref="DeleteForDocumentAsync"/> removes any partial chunks (no partial index). The embedding is
/// written via raw SQL with a text→<c>vector</c> cast (EF does not map the pgvector column on EF Core 10);
/// the owning session is stamped explicitly from the ambient <see cref="ISessionContext"/>.
/// </summary>
public sealed class ChunkRepository(RagBookDbContext dbContext, ISessionContext sessionContext) : IChunkRepository
{
    /// <inheritdoc />
    public async Task ReplaceForDocumentAsync(Document document, IReadOnlyList<Chunk> chunks, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        await DeleteExistingAsync(document.Id, cancellationToken);
        foreach (Chunk chunk in chunks)
        {
            await InsertAsync(chunk, cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken); // persists the tracked document's MarkReady
        await transaction.CommitAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task DeleteForDocumentAsync(Document document, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        await DeleteExistingAsync(document.Id, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken); // persists the tracked document's MarkFailed
        await transaction.CommitAsync(cancellationToken);
    }

    private Task DeleteExistingAsync(Guid documentId, CancellationToken cancellationToken)
    {
        return dbContext.Chunks
            .Where(chunk => chunk.DocumentId == documentId)
            .ExecuteDeleteAsync(cancellationToken);
    }

    private Task InsertAsync(Chunk chunk, CancellationToken cancellationToken)
    {
        string vectorLiteral = "[" + string.Join(
            ",",
            chunk.Embedding.ToArray().Select(value => value.ToString("R", CultureInfo.InvariantCulture))) + "]";

        return dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO chunks (id, document_id, user_session_id, index, text, page_number, embedding)
             VALUES ({chunk.Id}, {chunk.DocumentId}, {sessionContext.UserSessionId}, {chunk.Index}, {chunk.Text}, {chunk.PageNumber}, CAST({vectorLiteral} AS vector))
             """,
            cancellationToken);
    }
}
