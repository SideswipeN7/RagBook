using RagBook.API.ProblemDetails;
using RagBook.Modules.Folders.Features.CreateFolder;
using RagBook.Modules.Folders.Features.DeleteFolder;
using RagBook.Modules.Folders.Features.ListFolders;
using RagBook.Modules.Folders.Features.RenameFolder;
using RagBook.Shared.Results;
using Wolverine;

namespace RagBook.API.Endpoints;

/// <summary>
/// Maps the folder CRUD endpoints (US-09). Every operation is scoped to the current session by the
/// persistence layer; another session's folder resolves to 404, never 403 (FR-010). Failures carry a
/// stable <c>folder.*</c> code via ProblemDetails (constitution §II).
/// </summary>
public static class FolderEndpoints
{
    /// <summary>Maps <c>POST/GET /api/folders</c>, <c>PUT /api/folders/{id}/name</c>, <c>DELETE /api/folders/{id}</c>.</summary>
    public static IEndpointRouteBuilder MapFolderEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/folders");

        group.MapPost("/", async (CreateFolderRequest request, IMessageBus bus, CancellationToken cancellationToken) =>
        {
            Result<Guid> result = await bus.InvokeAsync<Result<Guid>>(
                new CreateFolderCommand(request.Name, request.ParentId),
                cancellationToken);

            return result.IsSuccess
                ? Results.Created($"/api/folders/{result.Value}", new CreateFolderResponse(result.Value))
                : ProblemResults.Problem(result.Error);
        });

        group.MapGet("/", async (IMessageBus bus, CancellationToken cancellationToken) =>
        {
            IReadOnlyList<FolderNode> folders =
                await bus.InvokeAsync<IReadOnlyList<FolderNode>>(new ListFoldersQuery(), cancellationToken);

            return Results.Ok(folders);
        });

        group.MapPut("/{id:guid}/name", async (Guid id, RenameFolderRequest request, IMessageBus bus, CancellationToken cancellationToken) =>
        {
            Result result = await bus.InvokeAsync<Result>(
                new RenameFolderCommand(id, request.Name),
                cancellationToken);

            return result.IsSuccess ? Results.NoContent() : ProblemResults.Problem(result.Error);
        });

        group.MapDelete("/{id:guid}", async (Guid id, IMessageBus bus, CancellationToken cancellationToken) =>
        {
            Result result = await bus.InvokeAsync<Result>(new DeleteFolderCommand(id), cancellationToken);

            return result.IsSuccess ? Results.NoContent() : ProblemResults.Problem(result.Error);
        });

        return endpoints;
    }
}
