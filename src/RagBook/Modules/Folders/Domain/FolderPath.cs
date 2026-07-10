namespace RagBook.Modules.Folders.Domain;

/// <summary>
/// A materialized folder path whose segments are folder identifiers (US-09 research D1). The string
/// form is <c>/{id}/{id}/…/</c> with a leading and trailing slash and lowercase <c>N</c>-format GUID
/// segments, so a subtree is an unambiguous prefix match and <see cref="Depth"/> equals the segment
/// count. Pure and immutable — the single tested place that formats and reasons about paths.
/// </summary>
public sealed class FolderPath
{
    private const char Separator = '/';

    private readonly IReadOnlyList<Guid> _segments;

    private FolderPath(IReadOnlyList<Guid> segments)
    {
        _segments = segments;
    }

    /// <summary>The ordered folder ids from root to this folder (inclusive).</summary>
    public IReadOnlyList<Guid> Segments => _segments;

    /// <summary>Nesting depth — root folders are depth 1.</summary>
    public int Depth => _segments.Count;

    /// <summary>The canonical string form <c>/{id}/…/</c> stored in the database.</summary>
    public string Value => Separator + string.Join(Separator, _segments.Select(id => id.ToString("N"))) + Separator;

    /// <summary>Builds the path of a root folder from its own id.</summary>
    public static FolderPath ForRoot(Guid id)
    {
        return new FolderPath([id]);
    }

    /// <summary>Returns a new path extending this one with <paramref name="id"/> as the next segment.</summary>
    public FolderPath Append(Guid id)
    {
        return new FolderPath([.. _segments, id]);
    }

    /// <summary>Parses a stored path string back into a <see cref="FolderPath"/>.</summary>
    public static FolderPath Parse(string value)
    {
        Guid[] segments = value
            .Split(Separator, StringSplitOptions.RemoveEmptyEntries)
            .Select(segment => Guid.ParseExact(segment, "N"))
            .ToArray();

        return new FolderPath(segments);
    }

    /// <summary>True when this path is an ancestor-or-self prefix of <paramref name="other"/>.</summary>
    public bool IsPrefixOf(FolderPath other)
    {
        return other.Value.StartsWith(Value, StringComparison.Ordinal);
    }

    /// <summary>True when <paramref name="id"/> appears as a segment of this path.</summary>
    public bool Contains(Guid id)
    {
        return _segments.Contains(id);
    }
}
