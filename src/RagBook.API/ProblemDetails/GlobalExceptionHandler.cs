using Microsoft.AspNetCore.Diagnostics;
using RagBook.Shared.Results;
using FluentValidationException = FluentValidation.ValidationException;

namespace RagBook.API.ProblemDetails;

/// <summary>
/// Last line of defense: turns any unhandled exception into an RFC 9457 ProblemDetails with a stable
/// <c>code</c> — never a naked 500 with a stack trace (constitution §II). Known shapes
/// (<see cref="DomainException"/>, validation) map to proper statuses; anything else becomes a
/// sanitized 500 with a correlation id, details logged and never returned.
/// </summary>
public sealed class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<GlobalExceptionHandler> logger)
    : IExceptionHandler
{
    /// <inheritdoc />
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        Error error = Resolve(exception, httpContext);

        httpContext.Response.StatusCode = ErrorStatusMapper.ToStatusCode(error.Type);

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails =
            {
                Status = httpContext.Response.StatusCode,
                Detail = error.Message,
                Extensions =
                {
                    ["code"] = error.Code,
                    ["traceId"] = CorrelationId.Current(httpContext),
                },
            },
        });
    }

    private Error Resolve(Exception exception, HttpContext httpContext)
    {
        switch (exception)
        {
            case DomainException domainException:
                return domainException.Error;
            case FluentValidationException:
                return Error.Validation("validation.failed", "One or more validation errors occurred.");
            default:
                logger.LogError(
                    exception,
                    "Unhandled exception for {Method} {Path} (traceId {TraceId})",
                    httpContext.Request.Method,
                    httpContext.Request.Path,
                    CorrelationId.Current(httpContext));

                return Error.Unexpected("error.unexpected", "An unexpected error occurred.");
        }
    }
}
