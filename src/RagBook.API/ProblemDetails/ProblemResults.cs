using RagBook.Shared.Results;

namespace RagBook.API.ProblemDetails;

/// <summary>
/// Builds <see cref="IResult"/> responses for failed results, always surfacing the stable
/// <see cref="Error.Code"/> so the frontend branches on the code, not the status or message.
/// </summary>
public static class ProblemResults
{
    /// <summary>Produces an RFC 9457 ProblemDetails response for <paramref name="error"/>.</summary>
    public static IResult Problem(Error error)
    {
        var statusCode = ErrorStatusMapper.ToStatusCode(error.Type);

        return Results.Problem(
            detail: error.Message,
            statusCode: statusCode,
            extensions: new Dictionary<string, object?>
            {
                ["code"] = error.Code,
            });
    }
}
