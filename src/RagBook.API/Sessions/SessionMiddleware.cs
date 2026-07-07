using Microsoft.Extensions.Options;
using RagBook.Shared.Sessions;

namespace RagBook.API.Sessions;

/// <summary>
/// Resolves the anonymous session for every request: reads and validates the session cookie, mints a
/// fresh GUID v4 when it is missing/forged/expired, publishes it into <see cref="ISessionInitializer"/>,
/// and (re)writes the cookie with the mandated flags and a refreshed 30-day expiry (AC-1, AC-2).
/// </summary>
public sealed class SessionMiddleware(RequestDelegate next, IOptions<SessionCookieOptions> options, TimeProvider timeProvider)
{
    /// <summary><see cref="HttpContext.Items"/> key flagging whether this request minted the session.</summary>
    public const string IsNewSessionItemKey = "ragbook.session.is_new";

    private readonly SessionCookieOptions _options = options.Value;

    /// <summary>Runs the middleware.</summary>
    public async Task InvokeAsync(HttpContext context, ISessionInitializer initializer)
    {
        var isNew = !TryReadSession(context, out var sessionId);
        if (isNew)
        {
            sessionId = Guid.NewGuid();
        }

        initializer.Initialize(sessionId);
        context.Items[IsNewSessionItemKey] = isNew;

        WriteCookie(context, sessionId);

        await next(context);
    }

    private bool TryReadSession(HttpContext context, out Guid sessionId)
    {
        sessionId = Guid.Empty;

        if (context.Request.Cookies.TryGetValue(_options.CookieName, out var raw)
            && Guid.TryParse(raw, out var parsed)
            && parsed != Guid.Empty)
        {
            sessionId = parsed;

            return true;
        }

        return false;
    }

    private void WriteCookie(HttpContext context, Guid sessionId)
    {
        context.Response.Cookies.Append(_options.CookieName, sessionId.ToString(), new CookieOptions
        {
            HttpOnly = true,
            Secure = _options.Secure,
            SameSite = _options.SameSite,
            IsEssential = true,
            Path = "/",
            Expires = timeProvider.GetUtcNow().Add(_options.SlidingExpiration),
        });
    }
}
