using RagBook.API.ProblemDetails;
using RagBook.Modules.Settings.Domain;
using RagBook.Modules.Settings.Features.DeleteApiKey;
using RagBook.Modules.Settings.Features.GetApiKeyStatus;
using RagBook.Modules.Settings.Features.SetApiKey;
using RagBook.Shared.Results;
using Wolverine;

namespace RagBook.API.Endpoints;

/// <summary>
/// Maps the BYOK settings endpoints (US-02). The user's key is validated upstream, then held only in the
/// session store (never persisted). Every response is <c>Cache-Control: no-store</c> so the mask/status
/// is not cached by intermediaries or the browser (FR-013). Only the mask ever leaves the server (AC-2).
/// </summary>
public static class SettingsEndpoints
{
    /// <summary>Maps <c>POST/GET/DELETE /api/settings/api-key</c>.</summary>
    public static IEndpointRouteBuilder MapSettingsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/settings/api-key");
        group.AddEndpointFilter(NoStoreFilter);

        group.MapPost("/", async (SetApiKeyRequest request, IMessageBus bus, CancellationToken cancellationToken) =>
        {
            Result<ApiKeyStatusResponse> result = await bus.InvokeAsync<Result<ApiKeyStatusResponse>>(
                new SetApiKeyCommand(request.ApiKey),
                cancellationToken);

            return result.IsSuccess ? Results.Ok(result.Value) : ProblemResults.Problem(result.Error);
        });

        group.MapGet("/", async (IMessageBus bus, CancellationToken cancellationToken) =>
        {
            ApiKeyStatusResponse status =
                await bus.InvokeAsync<ApiKeyStatusResponse>(new GetApiKeyStatusQuery(), cancellationToken);

            return Results.Ok(status);
        });

        group.MapDelete("/", async (IMessageBus bus, CancellationToken cancellationToken) =>
        {
            Result result = await bus.InvokeAsync<Result>(new DeleteApiKeyCommand(), cancellationToken);

            return result.IsSuccess ? Results.NoContent() : ProblemResults.Problem(result.Error);
        });

        return endpoints;
    }

    private static async ValueTask<object?> NoStoreFilter(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        context.HttpContext.Response.Headers.CacheControl = "no-store";

        return await next(context);
    }
}
