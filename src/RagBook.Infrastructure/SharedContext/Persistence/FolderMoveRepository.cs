using Microsoft.EntityFrameworkCore;
using RagBook.Modules.Folders.Domain;
using RagBook.Shared.Sessions;

namespace RagBook.Infrastructure.SharedContext.Persistence;

/// <summary>
/// EF Core implementation of <see cref="IFolderMoveRepository"/> (US-11). LINQ reads are session-filtered by the
/// global query filter. <see cref="MoveAsync"/> runs the re-parent + subtree path-prefix rewrite as one
/// transaction of two raw <c>UPDATE</c>s — which bypass the global filter, so both include an explicit
/// <c>user_session_id</c> predicate (constitution §III).
/// </summary>
public sealed class FolderMoveRepository(RagBookDbContext dbContext, ISessionContext sessionContext) : IFolderMoveRepository
{
    /// <inheritdoc />
    public Task<Folder?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        return dbContext.Folders.FirstOrDefaultAsync(folder => folder.Id == id, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<int> MaxSubtreeDepthAsync(string pathPrefix, CancellationToken cancellationToken)
    {
        // Session-filtered LINQ; depth is the segment count of the materialized path.
        List<string> paths = await dbContext.Folders
            .Where(folder => folder.Path.StartsWith(pathPrefix))
            .Select(folder => folder.Path)
            .ToListAsync(cancellationToken);

        return paths.Count == 0 ? 0 : paths.Max(path => FolderPath.Parse(path).Depth);
    }

    /// <inheritdoc />
    public Task<bool> SiblingExistsAsync(Guid? parentId, string name, Guid excludeId, CancellationToken cancellationToken)
    {
        IQueryable<Folder> query = dbContext.Folders
            .Where(folder => folder.Id != excludeId && folder.Name.ToLower() == name.ToLower());

        query = parentId is Guid parent
            ? query.Where(folder => folder.ParentId == parent)
            : query.Where(folder => folder.ParentId == null);

        return query.AnyAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task MoveAsync(Guid movedId, Guid? newParentId, string oldPrefix, string newPrefix, CancellationToken cancellationToken)
    {
        Guid session = sessionContext.UserSessionId;

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        // Rewrite the path prefix of the folder and every descendant. starts_with avoids LIKE escaping; the
        // explicit session predicate is mandatory for raw SQL (the global filter does not apply).
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE folders SET path = {newPrefix} || substring(path from {oldPrefix.Length + 1}) WHERE starts_with(path, {oldPrefix}) AND user_session_id = {session}",
            cancellationToken);

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE folders SET parent_id = {newParentId} WHERE id = {movedId} AND user_session_id = {session}",
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }
}
