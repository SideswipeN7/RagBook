using RagBook.API.ProblemDetails;
using RagBook.Modules.Documents.Errors;
using RagBook.Modules.Documents.Features.UploadDocument;
using RagBook.Shared.Results;
using Wolverine;

namespace RagBook.API.Endpoints;

/// <summary>
/// Maps the document upload endpoint (US-04). The file is read from the multipart body; its declared
/// name/content type are passed through but the type is validated from the content. Failures carry a
/// stable <c>document.*</c>/<c>quota.*</c>/<c>folder.*</c> code via ProblemDetails (constitution §II).
/// </summary>
public static class DocumentEndpoints
{
    /// <summary>Maps <c>POST /api/documents</c> (multipart/form-data).</summary>
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

        return endpoints;
    }
}
