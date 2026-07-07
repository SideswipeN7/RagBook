using Microsoft.AspNetCore.Http;

namespace RagBook.API.Sessions;

/// <summary>
/// Configuration for the anonymous session cookie. Bound from the <c>Session</c> section so every
/// tunable is config-driven — no magic numbers (constitution §III/§VII).
/// </summary>
public sealed class SessionCookieOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Session";

    /// <summary>Cookie name carrying the session identifier.</summary>
    public string CookieName { get; set; } = "ragbook_session";

    /// <summary>Sliding validity window in days; refreshed on every visit.</summary>
    public int SlidingExpirationDays { get; set; } = 30;

    /// <summary>Whether the cookie carries the <c>Secure</c> flag.</summary>
    public bool Secure { get; set; } = true;

    /// <summary>The cookie's <c>SameSite</c> mode.</summary>
    public SameSiteMode SameSite { get; set; } = SameSiteMode.Strict;

    /// <summary>The sliding window as a <see cref="TimeSpan"/>.</summary>
    public TimeSpan SlidingExpiration => TimeSpan.FromDays(SlidingExpirationDays);
}
