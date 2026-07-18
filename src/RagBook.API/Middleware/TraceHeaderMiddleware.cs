using RagBook.API.ProblemDetails;

namespace RagBook.API.Middleware;

/// <summary>
/// Stamps the per-request correlation id as the <c>X-Trace-Id</c> response header on every response (US-19). The
/// header is set from <see cref="CorrelationId.Current"/> just before the response is sent, so it matches the
/// <c>traceId</c> ProblemDetails extension on error responses and lets a caller quote one id for support.
/// </summary>
public sealed class TraceHeaderMiddleware(RequestDelegate next)
{
    private const string HeaderName = "X-Trace-Id";

    /// <summary>Registers the header write, then invokes the rest of the pipeline.</summary>
    public Task InvokeAsync(HttpContext httpContext)
    {
        httpContext.Response.OnStarting(static state =>
        {
            var context = (HttpContext)state;
            context.Response.Headers[HeaderName] = CorrelationId.Current(context);

            return Task.CompletedTask;
        }, httpContext);

        return next(httpContext);
    }
}
