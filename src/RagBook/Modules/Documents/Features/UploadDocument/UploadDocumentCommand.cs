using RagBook.Shared.Messaging;

namespace RagBook.Modules.Documents.Features.UploadDocument;

/// <summary>
/// Uploads a single file (PDF/TXT/MD) into a folder (or the root) for the current session (US-04).
/// The declared content type is carried for reference but is **not** trusted — the type is detected
/// from the content.
/// </summary>
/// <param name="FileName">The client-provided file name (used for display and Markdown/plain classification).</param>
/// <param name="DeclaredContentType">The client-declared content type (not trusted for validation).</param>
/// <param name="Content">The file bytes.</param>
/// <param name="FolderId">The target folder id, or <c>null</c> for the root.</param>
public sealed record UploadDocumentCommand(
    string FileName,
    string DeclaredContentType,
    byte[] Content,
    Guid? FolderId) : ICommand<DocumentResponse>;
