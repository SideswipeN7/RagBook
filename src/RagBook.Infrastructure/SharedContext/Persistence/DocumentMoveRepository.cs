using Microsoft.EntityFrameworkCore;
using RagBook.Modules.Documents.Domain;

namespace RagBook.Infrastructure.SharedContext.Persistence;

/// <summary>
/// EF Core implementation of <see cref="IDocumentMoveRepository"/> (US-10). The read flows through the context's
/// global session query filter, so a cross-session id reads as <c>null</c> → 404. The tracked entity's folder
/// change is persisted by <see cref="SaveChangesAsync"/>.
/// </summary>
public sealed class DocumentMoveRepository(RagBookDbContext dbContext) : IDocumentMoveRepository
{
    /// <inheritdoc />
    public Task<Document?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        return dbContext.Documents.FirstOrDefaultAsync(document => document.Id == id, cancellationToken);
    }

    /// <inheritdoc />
    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        return dbContext.SaveChangesAsync(cancellationToken);
    }
}
