using RagBook.Shared.Results;

namespace RagBook.API.ProblemDetails;

/// <summary>Maps an <see cref="ErrorType"/> to its RFC 9457 HTTP status code (constitution §II).</summary>
public static class ErrorStatusMapper
{
    /// <summary>Returns the HTTP status for the given <paramref name="errorType"/>.</summary>
    public static int ToStatusCode(ErrorType errorType)
    {
        return errorType switch
        {
            ErrorType.Validation => StatusCodes.Status400BadRequest,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            ErrorType.Unexpected => StatusCodes.Status500InternalServerError,
            ErrorType.Failure => StatusCodes.Status400BadRequest,
            _ => StatusCodes.Status400BadRequest,
        };
    }
}
