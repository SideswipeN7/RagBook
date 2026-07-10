using Microsoft.EntityFrameworkCore;
using RagBook.Modules.Documents.Domain;

namespace RagBook.Infrastructure.SharedContext.Persistence;

/// <summary>
/// Infrastructure implementation of the Documents module's <see cref="IFolderReference"/> seam (US-04).
/// Reads through the session query filter, so a folder owned by another session reads as absent — the
/// upload then returns not-found (404, FR-006) without the Documents module referencing the Folders
/// module's types.
/// </summary>
public sealed class FolderReference(RagBookDbContext dbContext) : IFolderReference
{
    /// <inheritdoc />
    public Task<bool> ExistsInSessionAsync(Guid folderId, CancellationToken cancellationToken)
    {
        return dbContext.Folders.AnyAsync(folder => folder.Id == folderId, cancellationToken);
    }
}
