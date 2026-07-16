using RagBook.API.ProblemDetails;
using RagBook.Modules.Documents.Domain;
using RagBook.Modules.Documents.Errors;
using RagBook.Modules.Documents.Features.BulkDelete;
using RagBook.Modules.Documents.Features.BulkMove;
using RagBook.Modules.Documents.Features.DeleteDocument;
using RagBook.Modules.Documents.Features.MoveDocument;
using RagBook.Modules.Documents.Features.UploadDocument;
using RagBook.Shared.Results;
using Wolverine;

namespace RagBook.API.Endpoints;

/// <summary>
/// Maps the document endpoints (US-04 upload, US-08 delete). The upload file is read from the multipart
/// body; its declared name/content type are passed through but the type is validated from the content.
/// Failures carry a stable <c>document.*</c>/<c>quota.*</c>/<c>folder.*</c> code via ProblemDetails (§II).
/// </summary>
public static class DocumentEndpoints
{
    /// <summary>Maps <c>POST /api/documents</c> (multipart) and <c>DELETE /api/documents/{id}</c>.</summary>
    public static IEndpointRouteBuilder MapDocumentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/documents", async (HttpRequest request, IMessageBus bus, CancellationToken cancellationToken) =>
        {
            if (!request.HasFormContentType)
            {
                return ProblemResults.Problem(DocumentErrors.EmptyFile);
            }

            var form = await request.ReadFormAsync(cancellationToken);
            var file = form.Files["file"];
            if (file is null || file.Length == 0)
            {
                return ProblemResults.Problem(DocumentErrors.EmptyFile);
            }

            Guid? folderId = Guid.TryParse(form["folderId"], out Guid parsed) ? parsed : null;

            byte[] content;
            await using (var stream = file.OpenReadStream())
            using (var buffer = new MemoryStream())
            {
                await stream.CopyToAsync(buffer, cancellationToken);
                content = buffer.ToArray();
            }

            Result<DocumentResponse> result = await bus.InvokeAsync<Result<DocumentResponse>>(
                new UploadDocumentCommand(file.FileName, file.ContentType, content, folderId),
                cancellationToken);

            return result.IsSuccess
                ? Results.Created($"/api/documents/{result.Value.Id}", result.Value)
                : ProblemResults.Problem(result.Error);
        });

        endpoints.MapDelete("/api/documents/{id:guid}", async (Guid id, IMessageBus bus, CancellationToken cancellationToken) =>
        {
            Result result = await bus.InvokeAsync<Result>(new DeleteDocumentCommand(id), cancellationToken);

            return result.IsSuccess ? Results.NoContent() : ProblemResults.Problem(result.Error);
        });

        // US-10 — move a document to a folder (or the root when folderId is null).
        endpoints.MapPatch("/api/documents/{id:guid}/folder", async (Guid id, MoveDocumentRequest request, IMessageBus bus, CancellationToken cancellationToken) =>
        {
            Result result = await bus.InvokeAsync<Result>(new MoveDocumentCommand(id, request.FolderId), cancellationToken);

            return result.IsSuccess ? Results.NoContent() : ProblemResults.Problem(result.Error);
        });

        // US-12 — bulk move: all-or-nothing move of many documents to one folder (or the root).
        endpoints.MapPost("/api/documents/bulk-move", async (BulkMoveRequest request, IMessageBus bus, CancellationToken cancellationToken) =>
        {
            BulkResult result = await bus.InvokeAsync<BulkResult>(
                new BulkMoveCommand(request.Ids ?? [], request.TargetFolderId), cancellationToken);

            return ToResult(result);
        });

        // US-12 — bulk delete: all-or-nothing delete of many documents (records + chunks cascade; quota −N).
        endpoints.MapPost("/api/documents/bulk-delete", async (BulkDeleteRequest request, IMessageBus bus, CancellationToken cancellationToken) =>
        {
            BulkResult result = await bus.InvokeAsync<BulkResult>(
                new BulkDeleteCommand(request.Ids ?? []), cancellationToken);

            return ToResult(result);
        });

        return endpoints;
    }

    /// <summary>Maps a <see cref="BulkResult"/> to its single wire outcome: 204 / 400 (ProblemResults) / 422 (failures[]).</summary>
    private static IResult ToResult(BulkResult result) => result.Outcome switch
    {
        BulkOutcome.Success => Results.NoContent(),
        BulkOutcome.BadRequest => ProblemResults.Problem(result.Error!),
        _ => BulkProblemResults.ValidationFailed(result.Failures),
    };
}

/// <summary>Body for <c>PATCH /api/documents/{id}/folder</c> (US-10). <c>null</c> moves the document to the root.</summary>
/// <param name="FolderId">The destination folder id, or <c>null</c> for the root.</param>
public sealed record MoveDocumentRequest(Guid? FolderId);

/// <summary>Body for <c>POST /api/documents/bulk-move</c> (US-12). <c>TargetFolderId</c> null moves to the root.</summary>
/// <param name="Ids">The documents to move.</param>
/// <param name="TargetFolderId">The destination folder id, or <c>null</c> for the root.</param>
public sealed record BulkMoveRequest(IReadOnlyList<Guid>? Ids, Guid? TargetFolderId);

/// <summary>Body for <c>POST /api/documents/bulk-delete</c> (US-12).</summary>
/// <param name="Ids">The documents to delete.</param>
public sealed record BulkDeleteRequest(IReadOnlyList<Guid>? Ids);
