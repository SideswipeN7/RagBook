namespace RagBook.Modules.Documents.Domain;

/// <summary>
/// A file name split into its base and extension, with the duplicate-suffix formatting (US-04 AC-5).
/// A collision within a folder produces <c>base (n).ext</c> with a space before the parenthesis and
/// <c>n</c> starting at 1 (clarify Q3). Pure and immutable — the one tested place for name formatting.
/// </summary>
public sealed record FileName(string Base, string Extension)
{
    /// <summary>The recomposed name (<see cref="Base"/> + <see cref="Extension"/>).</summary>
    public string Value => Base + Extension;

    /// <summary>
    /// Splits <paramref name="raw"/> into base + extension. The extension is the final dot-segment
    /// (including the dot); a name with no dot, or a leading-dot name like <c>.gitignore</c>, has an
    /// empty extension and the whole string as the base.
    /// </summary>
    public static FileName Parse(string raw)
    {
        int dot = raw.LastIndexOf('.');

        return dot > 0
            ? new FileName(raw[..dot], raw[dot..])
            : new FileName(raw, string.Empty);
    }

    /// <summary>Returns the name with the duplicate suffix applied: <c>base (n).ext</c>.</summary>
    public string WithSuffix(int number)
    {
        return $"{Base} ({number}){Extension}";
    }
}
