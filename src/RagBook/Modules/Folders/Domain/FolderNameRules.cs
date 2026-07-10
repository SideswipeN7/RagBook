using RagBook.Modules.Folders.Errors;
using RagBook.Shared.Results;

namespace RagBook.Modules.Folders.Domain;

/// <summary>
/// The config-driven rules for a folder name, derived from <c>FolderOptions</c> and passed to the
/// <see cref="Folder"/> factories so name validation stays config-driven without the domain depending
/// on <c>IOptions</c>. A name is normalized by trimming surrounding whitespace; the trimmed value is
/// what is stored, length-checked, and uniqueness-checked (FR-007).
/// </summary>
/// <param name="MaxNameLength">Maximum accepted length of a trimmed name.</param>
public sealed record FolderNameRules(int MaxNameLength)
{
    /// <summary>The reserved path separator; a name may never contain it (it delimits path segments).</summary>
    public const char PathSeparator = '/';

    /// <summary>
    /// Trims and validates <paramref name="name"/>. Returns the trimmed value on success, or
    /// <see cref="FolderErrors.InvalidName"/> when it is empty after trimming, exceeds
    /// <see cref="MaxNameLength"/>, or contains <see cref="PathSeparator"/>.
    /// </summary>
    public Result<string> Normalize(string? name)
    {
        string trimmed = (name ?? string.Empty).Trim();

        if (trimmed.Length == 0 || trimmed.Length > MaxNameLength || trimmed.Contains(PathSeparator))
        {
            return FolderErrors.InvalidName;
        }

        return trimmed;
    }
}
