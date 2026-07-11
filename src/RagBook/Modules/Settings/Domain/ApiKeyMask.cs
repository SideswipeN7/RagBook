namespace RagBook.Modules.Settings.Domain;

/// <summary>
/// Produces the display mask for a stored key: a recognizable prefix + the last four characters
/// (US-02 AC-2, FR-008). The full key is never rendered or returned; only this mask leaves the server.
/// </summary>
public static class ApiKeyMask
{
    private const string KnownPrefix = "sk-ant-api03-";
    private const string GenericPrefix = "sk-";
    private const string Ellipsis = "…";

    /// <summary>Masks <paramref name="fullKey"/> as <c>"{prefix}…{last4}"</c>, defensively hiding short input.</summary>
    public static string Mask(string fullKey)
    {
        if (string.IsNullOrEmpty(fullKey) || fullKey.Length < 4)
        {
            return Ellipsis;
        }

        string last4 = fullKey[^4..];
        string prefix = fullKey.StartsWith(KnownPrefix, StringComparison.Ordinal) ? KnownPrefix : GenericPrefix;

        return $"{prefix}{Ellipsis}{last4}";
    }
}
