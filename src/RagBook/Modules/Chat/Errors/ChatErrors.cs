using RagBook.Shared.Results;

namespace RagBook.Modules.Chat.Errors;

/// <summary>
/// Closed error catalog for the Chat module (US-13 seeds it; US-14 extends it). Codes are stable and
/// namespaced <c>chat.*</c> (constitution §II). The full RagBook catalog is owned by US-19.
/// </summary>
public static class ChatErrors
{
    /// <summary>
    /// The folder or document a scope names is not visible to the current session (nonexistent, deleted,
    /// or owned by another session). 404 — never disclose existence, consistent with session isolation (US-01).
    /// </summary>
    public static readonly Error ScopeNotFound =
        Error.NotFound("chat.scope_not_found", "The selected scope no longer exists.");

    /// <summary>The question is empty/whitespace or exceeds the maximum length (US-14).</summary>
    public static readonly Error InvalidQuestion =
        Error.Validation("chat.invalid_question", "The question is empty or too long.");

    /// <summary>The generation provider throttled the request (US-14 AC-5).</summary>
    public static readonly Error ProviderRateLimited =
        Error.RateLimited("chat.provider_rate_limited", "The AI provider is rate-limiting requests. Please try again shortly.");

    /// <summary>The generation provider is unavailable or timed out (US-14 AC-5).</summary>
    public static readonly Error ProviderUnavailable =
        Error.Unavailable("chat.provider_unavailable", "The AI provider is temporarily unavailable. Please try again.");
}
