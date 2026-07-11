using RagBook.Modules.Tree.Features.GetTree;
using Wolverine;

namespace RagBook.API.Endpoints;

/// <summary>
/// Maps the tree read endpoint (US-07). Returns the current session's folders + documents in one
/// response, scoped to the session by the persistence layer; the client composes the nested tree.
/// </summary>
public static class TreeEndpoints
{
    /// <summary>Maps <c>GET /api/tree</c>.</summary>
    public static IEndpointRouteBuilder MapTreeEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/tree", async (IMessageBus bus, CancellationToken cancellationToken) =>
        {
            TreeResponse response = await bus.InvokeAsync<TreeResponse>(new GetTreeQuery(), cancellationToken);

            return Results.Ok(response);
        });

        return endpoints;
    }
}
