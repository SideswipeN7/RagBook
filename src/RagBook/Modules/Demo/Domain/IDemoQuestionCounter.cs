namespace RagBook.Modules.Demo.Domain;

/// <summary>
/// Per-session, lifetime counter of demo questions (US-03 AC-2). Bound to the ambient session. The count persists
/// for the session's life (a long retention window), not a sliding rate window — the per-IP hourly limit
/// (<see cref="IDemoIpThrottle"/>) is the separate rate control.
/// </summary>
public interface IDemoQuestionCounter
{
    /// <summary>How many demo questions the current session has already asked.</summary>
    int Asked();

    /// <summary>How many demo questions the current session has left before the limit.</summary>
    int Remaining();

    /// <summary>
    /// Registers one demo question for the current session; returns <c>true</c> when it is within the limit, or
    /// <c>false</c> when the session has already used its whole allowance (no question is counted in that case).
    /// </summary>
    bool TryConsume();
}
