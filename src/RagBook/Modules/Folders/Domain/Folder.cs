using RagBook.Modules.Folders.Errors;
using RagBook.Shared.Auditing;
using RagBook.Shared.Results;
using RagBook.Shared.Sessions;

namespace RagBook.Modules.Folders.Domain;

/// <summary>
/// A session-owned node in the document tree (US-09). Hierarchy is a materialized path whose segments
/// are folder ids (<see cref="FolderPath"/>), so a rename is O(1) — it changes only <see cref="Name"/>
/// and never touches <see cref="Path"/>, <see cref="ParentId"/>, or any descendant. Construction goes
/// through <see cref="Result"/>-returning factories; the aggregate never throws for domain failures
/// (constitution §II). <see cref="UserSessionId"/> is stamped centrally on insert; per-parent name
/// uniqueness is enforced at the database boundary, not here.
/// </summary>
public sealed class Folder : ISessionOwned, IAuditable
{
    private Folder(Guid id, string name, Guid? parentId, string path)
    {
        Id = id;
        Name = name;
        ParentId = parentId;
        Path = path;
    }

    // Required by EF Core for materialization.
    private Folder()
    {
        Name = string.Empty;
        Path = string.Empty;
    }

    /// <summary>Identity (GUID v4); the last segment of <see cref="Path"/>.</summary>
    public Guid Id { get; private set; }

    /// <summary>Display name — trimmed, non-empty, within the configured length, no path separator.</summary>
    public string Name { get; private set; }

    /// <summary>Parent folder id, or <c>null</c> for a root folder.</summary>
    public Guid? ParentId { get; private set; }

    /// <summary>Materialized path <c>/{id}/…/</c> (segments are ids). Immutable after creation.</summary>
    public string Path { get; private set; }

    /// <inheritdoc />
    public Guid UserSessionId { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset CreatedAt { get; set; }

    /// <inheritdoc />
    public string CreatedBy { get; set; } = string.Empty;

    /// <inheritdoc />
    public DateTimeOffset? ModifiedAt { get; set; }

    /// <inheritdoc />
    public string? ModifiedBy { get; set; }

    /// <summary>Creates a root folder (no parent), validating the name (AC-1, AC-6).</summary>
    public static Result<Folder> CreateRoot(string name, FolderNameRules rules)
    {
        Result<string> normalized = rules.Normalize(name);
        if (normalized.IsFailure)
        {
            return Result.Failure<Folder>(normalized.Error);
        }

        var id = Guid.NewGuid();

        return new Folder(id, normalized.Value, parentId: null, FolderPath.ForRoot(id).Value);
    }

    /// <summary>
    /// Creates a child of <paramref name="parent"/>, validating the name (AC-6) and rejecting nesting
    /// beyond <paramref name="maxDepth"/> with <see cref="FolderErrors.MaxDepthExceeded"/> (AC-2).
    /// </summary>
    public static Result<Folder> CreateChild(Folder parent, string name, FolderNameRules rules, int maxDepth)
    {
        Result<string> normalized = rules.Normalize(name);
        if (normalized.IsFailure)
        {
            return Result.Failure<Folder>(normalized.Error);
        }

        FolderPath parentPath = FolderPath.Parse(parent.Path);
        if (parentPath.Depth >= maxDepth)
        {
            return FolderErrors.MaxDepthExceeded;
        }

        var id = Guid.NewGuid();

        return new Folder(id, normalized.Value, parent.Id, parentPath.Append(id).Value);
    }

    /// <summary>
    /// Renames the folder, validating the new name (AC-6). Changes only <see cref="Name"/> — the path,
    /// parent, and descendants are untouched (AC-4). Renaming to the current (post-trim) name succeeds.
    /// </summary>
    public Result Rename(string newName, FolderNameRules rules)
    {
        Result<string> normalized = rules.Normalize(newName);
        if (normalized.IsFailure)
        {
            return Result.Failure(normalized.Error);
        }

        Name = normalized.Value;

        return Result.Success();
    }
}
