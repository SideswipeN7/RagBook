namespace RagBook.Modules.Documents.Domain;

/// <summary>
/// Stores document binaries **outside** the relational database (US-04 FR-009). One seam covers a local
/// mounted volume in development and cloud object storage in production; the returned storage path is an
/// opaque key persisted on the <see cref="Document"/>. The display file name is deduplicated separately,
/// so the on-storage key never collides.
/// </summary>
public interface IFileStorage
{
    /// <summary>Stores <paramref name="content"/> and returns its opaque storage path/key.</summary>
    Task<string> SaveAsync(Stream content, string suggestedName, CancellationToken cancellationToken);

    /// <summary>Opens the stored content for reading.</summary>
    Task<Stream> OpenReadAsync(string storagePath, CancellationToken cancellationToken);

    /// <summary>Deletes the stored content (used for orphan cleanup when an upload is unwound — FR-012).</summary>
    Task DeleteAsync(string storagePath, CancellationToken cancellationToken);
}
