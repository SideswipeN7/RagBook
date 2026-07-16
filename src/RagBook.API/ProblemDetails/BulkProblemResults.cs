using RagBook.Modules.Documents.Domain;
using RagBook.Modules.Documents.Errors;

namespace RagBook.API.ProblemDetails;

/// <summary>
/// Builds the <c>422 Unprocessable Entity</c> response for an all-or-nothing bulk validation failure (US-12).
/// <c>ErrorStatusMapper</c> has no <c>422</c> and a single <see cref="Modules.Documents.Domain.BulkFailure"/> list
/// cannot travel through <see cref="ProblemResults.Problem"/>, so this helper emits the ProblemDetails directly:
/// the stable <c>code</c> (<see cref="DocumentErrors.BulkValidationFailedCode"/>) plus a <c>failures[]</c>
/// extension of <c>{ id, code }</c> objects (and the usual <c>traceId</c>), so the frontend branches on the code
/// and marks exactly the offending items.
/// </summary>
public static class BulkProblemResults
{
    /// <summary>Produces a <c>422</c> ProblemDetails naming every item that blocked the bulk operation.</summary>
    public static IResult ValidationFailed(IReadOnlyList<BulkFailure> failures)
    {
        var failurePayload = failures
            .Select(failure => new { id = failure.Id, code = failure.Code })
            .ToArray();

        return Results.Problem(
            detail: "One or more selected items could not be processed.",
            statusCode: StatusCodes.Status422UnprocessableEntity,
            extensions: new Dictionary<string, object?>
            {
                ["code"] = DocumentErrors.BulkValidationFailedCode,
                ["failures"] = failurePayload,
                ["traceId"] = System.Diagnostics.Activity.Current?.Id,
            });
    }
}
