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
}
