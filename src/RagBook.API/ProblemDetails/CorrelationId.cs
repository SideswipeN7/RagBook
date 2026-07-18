using System.Diagnostics;

namespace RagBook.API.ProblemDetails;

/// <summary>
/// The single source of the per-request correlation id used across every error surface (US-19). It is the current
/// W3C <see cref="Activity"/> id (populated for every request by ASP.NET Core / OpenTelemetry), falling back to the
/// framework <see cref="HttpContext.TraceIdentifier"/> only if no activity is present. The same value is emitted as
/// the <c>traceId</c> ProblemDetails extension, the <c>X-Trace-Id</c> response header, and — via OTel log scopes —
/// the server logs, so a reported id is traceable end-to-end.
/// </summary>
public static class CorrelationId
{
    /// <summary>The correlation id for the current request.</summary>
    public static string Current(HttpContext httpContext)
    {
        return Activity.Current?.Id ?? httpContext.TraceIdentifier;
    }
}
