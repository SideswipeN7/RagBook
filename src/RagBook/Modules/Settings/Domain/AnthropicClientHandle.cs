namespace RagBook.Modules.Settings.Domain;

/// <summary>
/// A generation client bound to a resolved session key. US-02 only needs its existence (the guard
/// succeeds when a key is present); US-14 will expand this into the real streaming chat client. The
/// key it carries is never logged or surfaced.
/// </summary>
public sealed class AnthropicClientHandle
{
    /// <summary>Creates a handle over the resolved <paramref name="apiKey"/> for the current session.</summary>
    public AnthropicClientHandle(string apiKey)
    {
        ApiKey = apiKey;
    }

    /// <summary>The resolved session key used to authenticate generation calls (consumed by US-14).</summary>
    public string ApiKey { get; }
}
