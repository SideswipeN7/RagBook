using RagBook.API.ProblemDetails;
using RagBook.Modules.Documents.Errors;
using RagBook.Modules.Documents.Features.DeleteDocument;
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

        return endpoints;
    }
}
