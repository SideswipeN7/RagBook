using System.Text.Json;
using RagBook.Modules.Documents.Domain;
using RagBook.Shared.Sessions;

namespace RagBook.API.Endpoints;

/// <summary>
/// Maps the SSE status stream (US-06). While subscribed, the current session's client receives each
/// document status change (`ready`/`failed`) as it happens, so the tree flips without a reload. Scoped to
/// the session by the ambient <see cref="ISessionContext"/> (set by the session middleware).
/// </summary>
public static class DocumentStatusEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Maps <c>GET /api/documents/status/stream</c> (text/event-stream).</summary>
    public static IEndpointRouteBuilder MapDocumentStatusEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/documents/status/stream", async (
            HttpContext httpContext,
            ISessionContext sessionContext,
            IDocumentStatusNotifier notifier,
            CancellationToken cancellationToken) =>
        {
            httpContext.Response.Headers.ContentType = "text/event-stream";
            httpContext.Response.Headers.CacheControl = "no-cache";

            await foreach (DocumentStatusUpdate update in notifier.Subscribe(sessionContext.UserSessionId, cancellationToken))
            {
                string json = JsonSerializer.Serialize(update, JsonOptions);
                await httpContext.Response.WriteAsync($"data: {json}\n\n", cancellationToken);
                await httpContext.Response.Body.FlushAsync(cancellationToken);
            }
        });

        return endpoints;
    }
}
