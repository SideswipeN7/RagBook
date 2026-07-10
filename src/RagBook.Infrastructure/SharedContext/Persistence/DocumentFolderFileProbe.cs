using Microsoft.EntityFrameworkCore;
using RagBook.Modules.Folders.Domain;

namespace RagBook.Infrastructure.SharedContext.Persistence;

/// <summary>
/// US-04 implementation of the Folders module's <see cref="IFolderFileProbe"/>. It **replaces**
/// <c>NoFolderFilesProbe</c> and answers the "does this folder contain files?" arm of the US-09
/// delete-emptiness rule with a real query over <c>documents.folder_id</c> — so deleting a folder that
/// holds documents is now blocked (US-09 AC-5 closed end-to-end). Session scoping is inherited from the
/// global query filter.
/// </summary>
public sealed class DocumentFolderFileProbe(RagBookDbContext dbContext) : IFolderFileProbe
{
    /// <inheritdoc />
    public Task<bool> HasFilesAsync(Guid folderId, CancellationToken cancellationToken)
    {
        return dbContext.Documents.AnyAsync(document => document.FolderId == folderId, cancellationToken);
    }
}
