namespace RagBook.Modules.Demo.Domain;

/// <summary>
/// Per-IP hourly rate limit on demo requests (US-03 AC-3) — the backstop against one visitor draining the
/// application key across many sessions. A fixed hourly window; on exceed the caller emits <c>429</c> with a
/// <c>Retry-After</c> header. BYOK (non-demo) requests are never throttled here.
/// </summary>
public interface IDemoIpThrottle
{
    /// <summary>
    /// Records a demo request for <paramref name="ipAddress"/>. Returns <c>Allowed = true</c> within the hourly
    /// limit, or <c>Allowed = false</c> with <c>RetryAfterSeconds</c> to the window reset once the limit is hit.
    /// </summary>
    (bool Allowed, int RetryAfterSeconds) TryRegister(string ipAddress);
}
