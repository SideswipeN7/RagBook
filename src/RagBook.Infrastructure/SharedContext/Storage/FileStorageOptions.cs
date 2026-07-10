namespace RagBook.Infrastructure.SharedContext.Storage;

/// <summary>
/// Configuration for local blob storage. Bound from the <c>FileStorage</c> section so the storage root
/// is config-driven, not hard-coded (constitution §VII). Production swaps a cloud object-storage driver
/// behind <c>IFileStorage</c>; this options type is the local driver's only knob.
/// </summary>
public sealed class FileStorageOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "FileStorage";

    /// <summary>Filesystem root under which document blobs are stored (absolute or relative to the app).</summary>
    public string RootPath { get; set; } = ".blobstore";
}
