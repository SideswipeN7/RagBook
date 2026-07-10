using Microsoft.Extensions.Options;
using RagBook.Modules.Documents.Domain;
using RagBook.Shared.Sessions;

namespace RagBook.Infrastructure.SharedContext.Storage;

/// <summary>
/// <see cref="IFileStorage"/> over a local filesystem root (US-04 dev driver). Blobs are namespaced by
/// session and a generated id (<c>{sessionId}/{blobId}{ext}</c>), so on-disk names never collide and a
/// session's blobs are easy to locate. The returned storage path is that relative key; it is opaque to
/// the domain. Production replaces this with a cloud object-storage driver behind the same interface.
/// </summary>
public sealed class LocalFileStorage(IOptions<FileStorageOptions> options, ISessionContext sessionContext)
    : IFileStorage
{
    private string Root => Path.GetFullPath(options.Value.RootPath);

    /// <inheritdoc />
    public async Task<string> SaveAsync(Stream content, string suggestedName, CancellationToken cancellationToken)
    {
        string extension = Path.GetExtension(suggestedName);
        string relativePath = Path.Combine(sessionContext.UserSessionId.ToString("N"), $"{Guid.NewGuid():N}{extension}");
        string fullPath = Path.Combine(Root, relativePath);

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        await using (FileStream file = File.Create(fullPath))
        {
            await content.CopyToAsync(file, cancellationToken);
        }

        // Store the key with forward slashes so it is stable across OSes.
        return relativePath.Replace(Path.DirectorySeparatorChar, '/');
    }

    /// <inheritdoc />
    public Task<Stream> OpenReadAsync(string storagePath, CancellationToken cancellationToken)
    {
        Stream stream = File.OpenRead(Resolve(storagePath));

        return Task.FromResult(stream);
    }

    /// <inheritdoc />
    public Task DeleteAsync(string storagePath, CancellationToken cancellationToken)
    {
        string fullPath = Resolve(storagePath);
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }

        return Task.CompletedTask;
    }

    private string Resolve(string storagePath)
    {
        return Path.Combine(Root, storagePath.Replace('/', Path.DirectorySeparatorChar));
    }
}
