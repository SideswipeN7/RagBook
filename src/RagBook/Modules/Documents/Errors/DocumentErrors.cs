using RagBook.Modules.Documents.Domain;
using RagBook.Shared.Results;

namespace RagBook.Modules.Documents.Errors;

/// <summary>
/// Closed error catalog for the Documents module's upload surface (US-04). Codes are stable and
/// namespaced <c>document.*</c> (constitution §II); size/count/total breaches reuse the US-05
/// <see cref="QuotaErrors"/> (<c>quota.*</c>). The full RagBook catalog is owned by US-19.
/// </summary>
public static class DocumentErrors
{
    /// <summary>The content is neither a real PDF nor valid text (AC-2). The message lists the allowed formats.</summary>
    public static readonly Error UnsupportedFileType =
        Error.Validation(
            "document.unsupported_file_type",
            $"Unsupported file type. Allowed formats: {SupportedFileTypes.AllowedList}.");

    /// <summary>A 0-byte (empty) upload (FR-004).</summary>
    public static readonly Error EmptyFile =
        Error.Validation("document.empty_file", "The file is empty.");

    /// <summary>
    /// The target folder does not exist in the current session (FR-006). Reuses the stable
    /// <c>folder.not_found</c> code so the client sees one not-found contract, without the Documents
    /// module referencing the Folders module's error catalog.
    /// </summary>
    public static readonly Error TargetFolderNotFound =
        Error.NotFound("folder.not_found", "The target folder does not exist.");
}
