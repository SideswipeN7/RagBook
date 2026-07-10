using Microsoft.EntityFrameworkCore;
using RagBook.Modules.Folders.Domain;
using RagBook.Modules.Folders.Errors;
using RagBook.Shared.Persistence;
using RagBook.Shared.Results;

namespace RagBook.Infrastructure.SharedContext.Persistence;

/// <summary>
/// EF Core implementation of <see cref="IFolderRepository"/>. All reads flow through the context's
/// global query filter, so they are automatically scoped to the current session (a cross-session id
/// reads as <c>null</c> → 404). Expected persistence faults are translated to Folders error codes via
/// <see cref="FoldersExceptionHandler"/> — a unique violation becomes <c>folder.duplicate_name</c>
/// (AC-3), a child foreign-key violation on delete becomes <c>folder.not_empty</c> (AC-5).
/// </summary>
public sealed class FolderRepository(
    RagBookDbContext dbContext,
    IPersistenceExceptionClassifier exceptionClassifier)
    : IFolderRepository
{
    /// <inheritdoc />
    public Task AddAsync(Folder folder, CancellationToken cancellationToken)
    {
        dbContext.Folders.Add(folder);

        return SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<Folder?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        return dbContext.Folders.FirstOrDefaultAsync(folder => folder.Id == id, cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> HasChildrenAsync(Guid folderId, CancellationToken cancellationToken)
    {
        return dbContext.Folders.AnyAsync(folder => folder.ParentId == folderId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Folder>> ListForSessionAsync(CancellationToken cancellationToken)
    {
        return await dbContext.Folders
            .AsNoTracking()
            .OrderBy(folder => folder.Name.ToLower())
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public void Remove(Folder folder)
    {
        dbContext.Folders.Remove(folder);
    }

    /// <inheritdoc />
    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            PersistenceErrorKind kind = exceptionClassifier.Classify(exception);
            if (FoldersExceptionHandler.TryMap(kind, out Error error))
            {
                throw new DomainException(error);
            }

            throw;
        }
    }
}
