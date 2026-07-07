using Microsoft.EntityFrameworkCore;
using RagBook.Modules.Session.Domain;
using RagBook.Modules.Session.Errors;
using RagBook.Shared.Persistence;
using RagBook.Shared.Results;

namespace RagBook.Infrastructure.SharedContext.Persistence;

/// <summary>
/// EF Core implementation of <see cref="ISessionResourceRepository"/>. All reads flow through the
/// context's global query filter, so they are automatically scoped to the current session. Expected
/// persistence faults are translated to Session error codes via <see cref="SessionExceptionHandler"/>.
/// </summary>
public sealed class SessionResourceRepository(
    RagBookDbContext dbContext,
    IPersistenceExceptionClassifier exceptionClassifier)
    : ISessionResourceRepository
{
    /// <inheritdoc />
    public async Task AddAsync(SessionResource resource, CancellationToken cancellationToken)
    {
        dbContext.SessionResources.Add(resource);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is DbUpdateException)
        {
            var kind = exceptionClassifier.Classify(exception);
            if (SessionExceptionHandler.TryMap(kind, out Error error))
            {
                throw new DomainException(error);
            }

            throw;
        }
    }

    /// <inheritdoc />
    public Task<SessionResource?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        return dbContext.SessionResources
            .AsNoTracking()
            .FirstOrDefaultAsync(resource => resource.Id == id, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SessionResource>> ListAsync(CancellationToken cancellationToken)
    {
        return await dbContext.SessionResources
            .AsNoTracking()
            .OrderBy(resource => resource.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<int> CountAsync(CancellationToken cancellationToken)
    {
        return dbContext.SessionResources.CountAsync(cancellationToken);
    }
}
