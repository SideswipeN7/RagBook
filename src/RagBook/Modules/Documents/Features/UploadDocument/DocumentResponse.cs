namespace RagBook.Modules.Documents.Features.UploadDocument;

/// <summary>The created document returned by a successful upload (US-04).</summary>
/// <param name="Id">Document identity.</param>
/// <param name="FileName">The stored name (post duplicate-suffix).</param>
/// <param name="ContentType">The detected canonical content type.</param>
/// <param name="SizeBytes">File size in bytes.</param>
/// <param name="Status">Lifecycle state — <c>Processing</c> at upload.</param>
/// <param name="FolderId">Owning folder, or <c>null</c> at the root.</param>
/// <param name="UploadedAt">When the upload was recorded.</param>
public sealed record DocumentResponse(
    Guid Id,
    string FileName,
    string ContentType,
    long SizeBytes,
    string Status,
    Guid? FolderId,
    DateTimeOffset UploadedAt);
