using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RagBook.Infrastructure.SharedContext.Persistence;
using RagBook.Modules.Demo;
using RagBook.Modules.Demo.Domain;
using RagBook.Modules.Documents.Domain;
using RagBook.Modules.Documents.Features.UploadDocument;
using RagBook.Modules.Documents.Processing;
using RagBook.Shared.Results;
using RagBook.Shared.Sessions;

namespace RagBook.Infrastructure.SharedContext.Demo;

/// <summary>
/// Seeds the read-only, globally-visible demo documents (US-03). Runs under the sentinel
/// <see cref="DemoConstants.DemoSessionId"/> so the seeded rows are stamped with a consistent owner; each missing
/// document (checked by fixed id, ignoring the session filter) is stored as a blob, inserted with
/// <see cref="Document.CreateDemo"/> (<c>Origin = Demo</c>), and indexed through the normal US-06 processing
/// pipeline (extract → chunk → embed → ready) so demo answers are grounded exactly like user uploads. Idempotent
/// by fixed id ⇒ a no-op on an already-seeded database and across restarts.
/// </summary>
public sealed class DemoDocumentSeeder(
    RagBookDbContext dbContext,
    IFileStorage fileStorage,
    ISessionInitializer sessionInitializer,
    ProcessDocumentHandler processor,
    TimeProvider timeProvider,
    IOptions<DemoOptions> options,
    ILogger<DemoDocumentSeeder> logger)
    : IDemoDocumentSeeder
{
    /// <inheritdoc />
    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        DemoOptions settings = options.Value;
        if (settings.Documents.Count == 0)
        {
            return;
        }

        // The seed rows are owned by the sentinel demo session; the stamping interceptor writes this on insert.
        sessionInitializer.Initialize(DemoConstants.DemoSessionId);

        foreach (DemoDocumentManifest manifest in settings.Documents)
        {
            Document? existing = await dbContext.Documents
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(document => document.Id == manifest.Id, cancellationToken);
            if (existing is not null)
            {
                // Already seeded. If a prior run inserted the row but its indexing did not finish (e.g. a transient
                // embedding failure left it Processing/Failed), re-drive processing — it is idempotent (chunks are
                // replaced) — so a demo document never stays permanently unready.
                if (existing.Status != DocumentStatus.Ready)
                {
                    await processor.Handle(new DocumentUploaded(manifest.Id), cancellationToken);
                }

                continue;
            }

            byte[] content = Encoding.UTF8.GetBytes(manifest.Text);
            string storagePath = await fileStorage.SaveAsync(
                new MemoryStream(content), manifest.FileName, cancellationToken);

            Result<Document> created = Document.CreateDemo(
                manifest.Id,
                content.Length,
                manifest.FileName,
                manifest.ContentType,
                storagePath,
                timeProvider.GetUtcNow());
            if (created.IsFailure)
            {
                logger.LogWarning(
                    "Skipping demo document {DemoDocumentId} ({FileName}): {Code}",
                    manifest.Id, manifest.FileName, created.Error.Code);
                continue;
            }

            dbContext.Documents.Add(created.Value);
            await dbContext.SaveChangesAsync(cancellationToken);

            // Index it exactly like an upload (chunk + embed + mark ready); the handler bridges the demo session.
            await processor.Handle(new DocumentUploaded(manifest.Id), cancellationToken);
        }
    }
}
