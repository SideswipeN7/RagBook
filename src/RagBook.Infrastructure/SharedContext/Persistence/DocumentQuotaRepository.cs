using Microsoft.EntityFrameworkCore;
using RagBook.Modules.Documents.Domain;
using RagBook.Modules.Documents.Errors;
using RagBook.Shared.Persistence;
using RagBook.Shared.Results;
using RagBook.Shared.Sessions;

namespace RagBook.Infrastructure.SharedContext.Persistence;

/// <summary>
/// EF Core implementation of <see cref="IDocumentQuotaRepository"/>. All reads flow through the
/// context's global query filter (scoped to the current session) and additionally exclude
/// <see cref="DocumentOrigin.Demo"/> documents. <see cref="TryAddWithinQuotaAsync"/> makes the
/// quota-check-and-insert atomic with a transaction-scoped PostgreSQL advisory lock keyed by the
/// session id, so two concurrent uploads at the boundary admit at most one (AC-5).
/// </summary>
public sealed class DocumentQuotaRepository(
    RagBookDbContext dbContext,
    ISessionContext sessionContext,
    IPersistenceExceptionClassifier exceptionClassifier)
    : IDocumentQuotaRepository
{
    /// <inheritdoc />
    public Task<int> CountAsync(CancellationToken cancellationToken)
    {
        return QuotaCounting().CountAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<long> SumSizeBytesAsync(CancellationToken cancellationToken)
    {
        // SUM over an empty set is NULL in SQL; EF coalesces to 0 for the non-nullable projection.
        return await QuotaCounting().SumAsync(document => document.SizeBytes, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Result> TryAddWithinQuotaAsync(
        Document document,
        QuotaLimits limits,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        // Serialize concurrent admits for THIS session only; released automatically at commit/rollback.
        long lockKey = AdvisoryLockKey(sessionContext.UserSessionId);
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({lockKey})",
            cancellationToken);

        // Re-read usage UNDER the lock — this is what makes the check-and-insert atomic.
        int usedDocuments = await CountAsync(cancellationToken);
        long usedBytes = await SumSizeBytesAsync(cancellationToken);
        var snapshot = new QuotaSnapshot(usedDocuments, usedBytes, limits);

        Result admission = snapshot.CanAdmit(document.SizeBytes);
        if (admission.IsFailure)
        {
            await transaction.RollbackAsync(cancellationToken);

            return admission;
        }

        dbContext.Documents.Add(document);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return Result.Success();
        }
        catch (DbUpdateException exception)
        {
            await transaction.RollbackAsync(cancellationToken);

            var kind = exceptionClassifier.Classify(exception);
            if (DocumentsExceptionHandler.TryMap(kind, out Error error))
            {
                return Result.Failure(error);
            }

            throw;
        }
    }

    private IQueryable<Document> QuotaCounting()
    {
        // Session scoping is inherited from the global query filter; exclude demo documents (US-05).
        return dbContext.Documents
            .AsNoTracking()
            .Where(document => document.Origin != DocumentOrigin.Demo);
    }

    private static long AdvisoryLockKey(Guid sessionId)
    {
        Span<byte> bytes = stackalloc byte[16];
        sessionId.TryWriteBytes(bytes);

        // Reinterpret the first 8 bytes as a stable, deterministic per-session lock key. A collision
        // across sessions only briefly parks unrelated uploads; correctness holds because the in-lock
        // re-count is always session-filtered.
        return BitConverter.ToInt64(bytes);
    }
}
