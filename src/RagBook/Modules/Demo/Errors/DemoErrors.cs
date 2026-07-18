using RagBook.Shared.Results;

namespace RagBook.Modules.Demo.Errors;

/// <summary>
/// Closed error catalog for demo mode (US-03). Codes are stable and namespaced <c>chat.demo_*</c> (the demo failures
/// surface on the chat surface). The full RagBook catalog is owned by US-19.
/// </summary>
public static class DemoErrors
{
    /// <summary>
    /// The session has used its whole demo-question allowance (AC-2). A rate-limit outcome (→ 429) — well-formed,
    /// but throttled; the frontend branches on the code to show "X / N pytań demo" and the BYOK nudge.
    /// </summary>
    public static readonly Error LimitReached =
        Error.RateLimited("chat.demo_limit_reached", "You have used all demo questions for this session.");

    /// <summary>
    /// The visitor's IP has exceeded the hourly demo rate (AC-3). A rate-limit outcome (→ 429); the endpoint adds a
    /// <c>Retry-After</c> header with the seconds until the window resets.
    /// </summary>
    public static readonly Error IpRateLimited =
        Error.RateLimited("chat.demo_rate_limited", "Too many demo requests from your network. Please try again later.");

    /// <summary>
    /// Demo mode cannot generate right now — the application key is unset or its budget is exhausted (edge case).
    /// An unavailable outcome (→ 503), surfaced as a readable "demo temporarily unavailable", never a raw 500.
    /// </summary>
    public static readonly Error Unavailable =
        Error.Unavailable("chat.demo_unavailable", "Demo mode is temporarily unavailable. Please try again later.");
}
