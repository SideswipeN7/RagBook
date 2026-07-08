using RagBook.Modules.Documents.Features.GetQuota;
using Wolverine;

namespace RagBook.API.Endpoints;

/// <summary>
/// Maps the quota read endpoint. The state is scoped to the current session by the persistence layer,
/// so one session's counter never reflects another's documents (AC-1, FR-006).
/// </summary>
public static class QuotaEndpoints
{
    /// <summary>Maps <c>GET /api/quota</c>.</summary>
    public static IEndpointRouteBuilder MapQuotaEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/quota", async (IMessageBus bus, CancellationToken cancellationToken) =>
        {
            QuotaStateResponse state =
                await bus.InvokeAsync<QuotaStateResponse>(new GetQuotaQuery(), cancellationToken);

            return Results.Ok(state);
        });

        return endpoints;
    }
}
