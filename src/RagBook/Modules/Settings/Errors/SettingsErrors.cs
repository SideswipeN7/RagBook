using RagBook.Shared.Results;

namespace RagBook.Modules.Settings.Errors;

/// <summary>
/// Closed error catalog for the Settings module (US-02 BYOK). Codes are stable and namespaced
/// <c>settings.*</c> (constitution §II). The full RagBook catalog is owned by US-19.
/// </summary>
public static class SettingsErrors
{
    /// <summary>
    /// The key is empty/malformed, or the provider rejected it (no credit, revoked, wrong value).
    /// Both cases share one code so the frontend maps a single "invalid key" message (FR-003, FR-004).
    /// </summary>
    public static readonly Error InvalidApiKey =
        Error.Validation("settings.invalid_api_key", "The API key is invalid or was rejected by the provider.");

    /// <summary>
    /// The provider could not be reached to validate the key (timeout / 5xx / network). Transient and
    /// distinct from a rejection, so the user is told to retry rather than "invalid" (FR-004a).
    /// </summary>
    public static readonly Error ValidationUnavailable =
        Error.Unavailable("settings.validation_unavailable", "Could not verify the key right now. Please try again.");

    /// <summary>Generation was attempted without an active key for the session (FR-009).</summary>
    public static readonly Error ApiKeyMissing =
        Error.Unauthorized("settings.api_key_missing", "No API key is configured for this session.");

    /// <summary>Too many save/validate attempts from one session in the throttle window (FR-004b).</summary>
    public static readonly Error TooManyAttempts =
        Error.RateLimited("settings.too_many_attempts", "Too many attempts. Please wait a moment and try again.");
}
