using RagBook.API.Sessions;
using RagBook.Modules.Session.Features;
using RagBook.Modules.Session.Features.ListResources;
using Wolverine;

namespace RagBook.API.Endpoints;

/// <summary>Maps the session-state endpoint. The cookie itself is issued by <see cref="SessionMiddleware"/>.</summary>
public static class SessionEndpoints
{
    /// <summary>Maps <c>GET /api/session</c>.</summary>
    public static IEndpointRouteBuilder MapSessionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/session", async (HttpContext httpContext, IMessageBus bus, CancellationToken cancellationToken) =>
        {
            IReadOnlyList<ResourceResponse> resources =
                await bus.InvokeAsync<IReadOnlyList<ResourceResponse>>(new ListResourcesQuery(), cancellationToken);

            var isNew = httpContext.Items.TryGetValue(SessionMiddleware.IsNewSessionItemKey, out var value)
                && value is true;

            return Results.Ok(new SessionStateResponse(isNew, resources.Count));
        });

        return endpoints;
    }
}
