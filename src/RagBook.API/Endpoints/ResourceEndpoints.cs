using RagBook.API.ProblemDetails;
using RagBook.Modules.Session.Features;
using RagBook.Modules.Session.Features.CreateResource;
using RagBook.Modules.Session.Features.GetResource;
using RagBook.Modules.Session.Features.ListResources;
using RagBook.Shared.Results;
using Wolverine;

namespace RagBook.API.Endpoints;

/// <summary>
/// Maps the reference session-owned resource endpoints. Every read is scoped to the current session
/// by the persistence layer; another session's resource resolves to 404, never 403 (AC-3).
/// </summary>
public static class ResourceEndpoints
{
    /// <summary>Maps <c>POST/GET /api/resources</c> and <c>GET /api/resources/{id}</c>.</summary>
    public static IEndpointRouteBuilder MapResourceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/resources");

        group.MapPost("/", async (CreateResourceRequest request, IMessageBus bus, CancellationToken cancellationToken) =>
        {
            Result<Guid> result =
                await bus.InvokeAsync<Result<Guid>>(new CreateResourceCommand(request.Name), cancellationToken);

            return result.IsSuccess
                ? Results.Created($"/api/resources/{result.Value}", new CreateResourceResponse(result.Value))
                : ProblemResults.Problem(result.Error);
        });

        group.MapGet("/{id:guid}", async (Guid id, IMessageBus bus, CancellationToken cancellationToken) =>
        {
            Result<ResourceResponse> result =
                await bus.InvokeAsync<Result<ResourceResponse>>(new GetResourceQuery(id), cancellationToken);

            return result.IsSuccess
                ? Results.Ok(result.Value)
                : ProblemResults.Problem(result.Error);
        });

        group.MapGet("/", async (IMessageBus bus, CancellationToken cancellationToken) =>
        {
            IReadOnlyList<ResourceResponse> resources =
                await bus.InvokeAsync<IReadOnlyList<ResourceResponse>>(new ListResourcesQuery(), cancellationToken);

            return Results.Ok(resources);
        });

        return endpoints;
    }
}
