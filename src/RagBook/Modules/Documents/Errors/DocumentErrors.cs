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

    /// <summary>No such document in the current session (incl. a cross-session id or an already-deleted one — US-08).</summary>
    public static readonly Error NotFound =
        Error.NotFound("document.not_found", "The document does not exist.");

    /// <summary>
    /// The target folder does not exist in the current session (FR-006). Reuses the stable
    /// <c>folder.not_found</c> code so the client sees one not-found contract, without the Documents
    /// module referencing the Folders module's error catalog.
    /// </summary>
    public static readonly Error TargetFolderNotFound =
        Error.NotFound("folder.not_found", "The target folder does not exist.");

    /// <summary>
    /// The document is a read-only demo document (US-03 origin) and cannot be reorganised (US-10). A conflict
    /// with the resource's read-only nature (→ 409) — it exists and the request is well-formed.
    /// </summary>
    public static readonly Error ReadOnly =
        Error.Conflict("document.read_only", "This document is read-only and cannot be moved.");

    /// <summary>
    /// Top-level code for an all-or-nothing bulk failure (US-12). Not mapped by <c>ErrorStatusMapper</c> — the
    /// endpoint builds the <c>422</c> ProblemDetails directly (with a <c>failures[]</c> extension), because a
    /// single <see cref="Error"/> cannot carry the per-id list. Exposed as a bare code, not an <see cref="Error"/>.
    /// </summary>
    public const string BulkValidationFailedCode = "document.bulk_validation_failed";

    /// <summary>A bulk request with an empty id list (US-12 FR-006) — a plain 400, distinct from a per-id failure.</summary>
    public static readonly Error BulkEmpty =
        Error.Validation("document.bulk_empty", "The bulk operation has no documents.");

    /// <summary>A bulk id list exceeding the configured cap (US-12 FR-006) — a plain 400 with a distinct code.</summary>
    public static readonly Error BulkTooLarge =
        Error.Validation("document.bulk_too_large", "The bulk operation has too many documents.");
}
