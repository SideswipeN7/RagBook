namespace RagBook.Modules.Settings.Domain;

/// <summary>
/// The externally visible view of the session's key: its status and, when active, the mask. Never
/// carries the full key (US-02 AC-2). Single definition reused by the set and get handlers and the
/// endpoint response (analyze A1).
/// </summary>
/// <param name="Status">Either <c>"none"</c> or <c>"active"</c>.</param>
/// <param name="MaskedKey">The mask when <paramref name="Status"/> is <c>"active"</c>; otherwise <c>null</c>.</param>
public sealed record ApiKeyStatusResponse(string Status, string? MaskedKey)
{
    /// <summary>The response for a session with no stored key.</summary>
    public static ApiKeyStatusResponse None()
    {
        return new ApiKeyStatusResponse("none", null);
    }

    /// <summary>The response for a session with a stored key, carrying only its <paramref name="mask"/>.</summary>
    public static ApiKeyStatusResponse Active(string mask)
    {
        return new ApiKeyStatusResponse("active", mask);
    }
}
