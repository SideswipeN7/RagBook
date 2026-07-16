using RagBook.Shared.Results;

namespace RagBook.Modules.Folders.Errors;

/// <summary>
/// Closed error catalog for the Folders module. Handlers and the folder domain may only return codes
/// from this list; codes are stable and namespaced <c>folder.*</c> (constitution §II). The full
/// RagBook catalog is owned by US-19; this is the module's slice.
/// </summary>
public static class FolderErrors
{
    /// <summary>The name is empty after trimming, too long, or contains the path separator (AC-6).</summary>
    public static readonly Error InvalidName =
        Error.Validation("folder.invalid_name", "Folder name is empty, too long, or contains a '/'.");

    /// <summary>Creating the folder would nest it beyond the configured maximum depth (AC-2).</summary>
    public static readonly Error MaxDepthExceeded =
        Error.Validation("folder.max_depth_exceeded", "Maximum folder nesting depth reached.");

    /// <summary>A folder with this name already exists in the same parent, case-insensitively (AC-3).</summary>
    public static readonly Error DuplicateName =
        Error.Conflict("folder.duplicate_name", "A folder with this name already exists here.");

    /// <summary>The folder still holds subfolders and/or files, so it cannot be deleted (AC-5).</summary>
    public static readonly Error NotEmpty =
        Error.Conflict("folder.not_empty", "Folder is not empty. Delete or move its contents first.");

    /// <summary>No such folder exists in the current session (incl. a cross-session id — FR-010).</summary>
    public static readonly Error NotFound =
        Error.NotFound("folder.not_found", "The folder does not exist.");

    /// <summary>An infrastructure-level persistence conflict surfaced at the database boundary.</summary>
    public static readonly Error Conflict =
        Error.Conflict("folder.conflict", "The operation conflicted with a concurrent change. Please retry.");

    /// <summary>
    /// The move target is the folder itself or one of its descendants (US-11) — a cycle. 409, consistent with
    /// the other <c>folder.*</c> conflict errors.
    /// </summary>
    public static readonly Error CircularMove =
        Error.Conflict("folder.circular_move", "A folder cannot be moved into itself or one of its subfolders.");
}
