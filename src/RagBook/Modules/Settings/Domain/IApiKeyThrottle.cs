namespace RagBook.Modules.Settings.Domain;

/// <summary>
/// Per-session gate on save/validate attempts (US-02 FR-004b). Because each save triggers a paid
/// upstream call, the handler registers an attempt before validating; once the window limit is hit,
/// further attempts are refused without reaching the provider. Bound to the ambient session.
/// </summary>
public interface IApiKeyThrottle
{
    /// <summary>
    /// Records an attempt for the current session and returns <c>true</c> if it is within the limit,
    /// or <c>false</c> if the session has exceeded the allowed attempts in the current window.
    /// </summary>
    bool TryRegisterAttempt();
}
