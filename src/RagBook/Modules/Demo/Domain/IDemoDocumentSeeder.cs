namespace RagBook.Modules.Demo.Domain;

/// <summary>
/// Seeds the globally-visible, read-only demo documents at startup (US-03). Idempotent by fixed id — a no-op on an
/// already-seeded database and correct on a clean database and across restarts. Runs under the sentinel
/// <see cref="DemoConstants.DemoSessionId"/> so the seeded rows have a consistent owner, and indexes each document
/// through the normal chunk+embed pipeline so demo answers are grounded exactly like user uploads.
/// </summary>
public interface IDemoDocumentSeeder
{
    /// <summary>Creates any missing demo documents (and their chunks); skips those that already exist.</summary>
    Task SeedAsync(CancellationToken cancellationToken);
}
