using RagBook.Shared.Results;

namespace RagBook.Modules.Settings.Domain;

/// <summary>
/// Produces a generation client bound to the current session's BYOK key. This is the seam future chat
/// (US-14) calls before generating; US-02 delivers only the guard: no key → <c>settings.api_key_missing</c>
/// (US-02 AC-3, FR-009). The concrete client type is fleshed out by US-14.
/// </summary>
public interface IAnthropicClientFactory
{
    /// <summary>
    /// Returns a client handle for the current session, or <see cref="Errors.SettingsErrors.ApiKeyMissing"/>
    /// when the session has no active key.
    /// </summary>
    Result<AnthropicClientHandle> CreateForSession();
}
