using Microsoft.EntityFrameworkCore;
using RagBook.Modules.Documents.Domain;
using RagBook.Modules.Documents.Errors;
using RagBook.Shared.Persistence;
using RagBook.Shared.Results;
using RagBook.Shared.Sessions;

namespace RagBook.Infrastructure.SharedContext.Persistence;

/// <summary>
/// EF Core implementation of <see cref="IDocumentUploadRepository"/> (US-04). It reuses the same
/// transaction-scoped session advisory lock as the US-05 quota admit; because that lock serializes a
/// session's uploads, it re-reads usage, computes the first free file name for the target folder, and
/// inserts once — no same-transaction 23505 retry (research D5). The two partial unique file-name
/// indexes remain as a backstop, mapped by <see cref="DocumentsExceptionHandler"/> if ever hit.
/// </summary>
public sealed class DocumentUploadRepository(
    RagBookDbContext dbContext,
    ISessionContext sessionContext,
    IPersistenceExceptionClassifier exceptionClassifier)
    : IDocumentUploadRepository
{
    /// <inheritdoc />
    public async Task<Result> AddUploadedWithinQuotaAsync(
        Document document,
        QuotaLimits limits,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        // Serialize this session's uploads (mirrors DocumentQuotaRepository); released at commit/rollback.
        long lockKey = AdvisoryLockKey(sessionContext.UserSessionId);
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({lockKey})",
            cancellationToken);

        // Quota re-check under the lock — count/total exactly as the US-05 admit.
        int usedDocuments = await QuotaCounting().CountAsync(cancellationToken);
        long usedBytes = await QuotaCounting().SumAsync(candidate => candidate.SizeBytes, cancellationToken);
        Result admission = new QuotaSnapshot(usedDocuments, usedBytes, limits).CanAdmit(document.SizeBytes);
        if (admission.IsFailure)
        {
            await transaction.RollbackAsync(cancellationToken);

            return admission;
        }

        // De-duplicate the file name within the target folder, under the lock (AC-5).
        await AssignFreeFileNameAsync(document, cancellationToken);

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

            PersistenceErrorKind kind = exceptionClassifier.Classify(exception);
            if (DocumentsExceptionHandler.TryMap(kind, out Error error))
            {
                return Result.Failure(error);
            }

            throw;
        }
    }

    private async Task AssignFreeFileNameAsync(Document document, CancellationToken cancellationToken)
    {
        var requested = FileName.Parse(document.FileName!);

        // Existing names in the same folder (session scoping inherited from the global query filter).
        List<string> taken = await dbContext.Documents
            .AsNoTracking()
            .Where(candidate => candidate.FolderId == document.FolderId && candidate.FileName != null)
            .Select(candidate => candidate.FileName!.ToLower())
            .ToListAsync(cancellationToken);

        var takenSet = new HashSet<string>(taken);
        if (!takenSet.Contains(requested.Value.ToLowerInvariant()))
        {
            return;
        }

        for (int suffix = 1; ; suffix++)
        {
            string candidate = requested.WithSuffix(suffix);
            if (!takenSet.Contains(candidate.ToLowerInvariant()))
            {
                document.RenameForSuffix(candidate);

                return;
            }
        }
    }

    private IQueryable<Document> QuotaCounting()
    {
        return dbContext.Documents
            .AsNoTracking()
            .Where(candidate => candidate.Origin != DocumentOrigin.Demo);
    }

    private static long AdvisoryLockKey(Guid sessionId)
    {
        Span<byte> bytes = stackalloc byte[16];
        sessionId.TryWriteBytes(bytes);

        // Same per-session key derivation as DocumentQuotaRepository, so the quota admit and the upload
        // admit take the SAME lock and never interleave for a session.
        return BitConverter.ToInt64(bytes);
    }
}
