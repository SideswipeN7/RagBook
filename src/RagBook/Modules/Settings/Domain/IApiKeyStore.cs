namespace RagBook.Modules.Settings.Domain;

/// <summary>
/// Session-scoped store for the user's BYOK generation key. The key lives only here (in memory),
/// never in the database (constitution §VII). The implementation is bound to the current session, so
/// callers never pass a session id — reads and writes target the ambient session only.
/// </summary>
public interface IApiKeyStore
{
    /// <summary>Returns the stored key for the current session, or <c>null</c> if none/expired.</summary>
    string? Get();

    /// <summary>Stores (or overwrites) the key for the current session with the configured TTL.</summary>
    void Set(string apiKey);

    /// <summary>Removes any key for the current session. No-op when absent (idempotent).</summary>
    void Remove();
}
