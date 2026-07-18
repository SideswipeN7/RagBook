using Microsoft.EntityFrameworkCore;
using RagBook.Modules.Documents.Domain;
using RagBook.Modules.Folders.Domain;
using RagBook.Modules.Tree.Domain;

namespace RagBook.Infrastructure.SharedContext.Persistence;

/// <summary>
/// EF Core implementation of <see cref="ITreeReader"/> (US-07). Runs exactly two session-scoped
/// <see cref="EntityFrameworkQueryableExtensions"/> queries — folders ordered by <c>LOWER(name)</c> and
/// documents (excluding demo) ordered by <c>uploaded_at</c> descending — so the whole tree loads with no
/// per-folder fan-out (FR-001/SC-001). Session scoping is inherited from the global query filter
/// (FR-012). Folder depth is computed from the materialized path after materialization (it is not a
/// translatable expression).
/// </summary>
public sealed class TreeReader(RagBookDbContext dbContext) : ITreeReader
{
    /// <inheritdoc />
    public async Task<TreeData> GetAsync(CancellationToken cancellationToken)
    {
        List<Folder> folders = await dbContext.Folders
            .AsNoTracking()
            .OrderBy(folder => folder.Name.ToLower())
            .ToListAsync(cancellationToken);

        List<Document> documents = await dbContext.Documents
            .AsNoTracking()
            .Where(document => document.Origin != DocumentOrigin.Demo)
            .OrderByDescending(document => document.UploadedAt)
            .ToListAsync(cancellationToken);

        // US-03 — the read-only demo documents are global (owned by the sentinel demo session), so they are read
        // by origin with the per-session filter bypassed; visible in every session's Demo section.
        List<Document> demoDocuments = await dbContext.Documents
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(document => document.Origin == DocumentOrigin.Demo)
            .OrderByDescending(document => document.UploadedAt)
            .ToListAsync(cancellationToken);

        List<TreeFolder> treeFolders = folders
            .Select(folder => new TreeFolder(folder.Id, folder.ParentId, folder.Name, FolderPath.Parse(folder.Path).Depth))
            .ToList();

        List<TreeDocument> treeDocuments = documents.Select(ToTreeDocument).ToList();
        List<TreeDocument> treeDemoDocuments = demoDocuments.Select(ToTreeDocument).ToList();

        return new TreeData(treeFolders, treeDocuments, treeDemoDocuments);
    }

    private static TreeDocument ToTreeDocument(Document document)
    {
        return new TreeDocument(
            document.Id,
            document.FolderId,
            document.FileName ?? string.Empty,
            document.ContentType ?? string.Empty,
            document.SizeBytes,
            document.Status.ToString(),
            document.ChunkCount,
            document.UploadedAt ?? document.CreatedAt,
            document.FailureReason);
    }
}
