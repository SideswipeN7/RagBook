using FluentAssertions;
using Microsoft.AspNetCore.Http;
using RagBook.API.ProblemDetails;
using RagBook.Shared.Results;
using Xunit;

namespace RagBook.Api.IntegrationTests.Settings;

/// <summary>
/// Unit tests for the two error categories US-02 adds to the shared kernel (analyze U1): the throttle
/// maps to 429 and the transient upstream failure maps to 503, without disturbing existing mappings.
/// </summary>
public sealed class ErrorStatusMapperTests
{
    [Fact]
    public void Should_Map_RateLimited_To_429()
    {
        ErrorStatusMapper.ToStatusCode(ErrorType.RateLimited).Should().Be(StatusCodes.Status429TooManyRequests);
    }

    [Fact]
    public void Should_Map_Unavailable_To_503()
    {
        ErrorStatusMapper.ToStatusCode(ErrorType.Unavailable).Should().Be(StatusCodes.Status503ServiceUnavailable);
    }

    [Theory]
    [InlineData(ErrorType.Validation, StatusCodes.Status400BadRequest)]
    [InlineData(ErrorType.NotFound, StatusCodes.Status404NotFound)]
    [InlineData(ErrorType.Conflict, StatusCodes.Status409Conflict)]
    [InlineData(ErrorType.Unauthorized, StatusCodes.Status401Unauthorized)]
    [InlineData(ErrorType.Forbidden, StatusCodes.Status403Forbidden)]
    [InlineData(ErrorType.Unexpected, StatusCodes.Status500InternalServerError)]
    public void Should_PreserveExistingMappings(ErrorType errorType, int expectedStatus)
    {
        ErrorStatusMapper.ToStatusCode(errorType).Should().Be(expectedStatus);
    }

    [Fact]
    public void Should_MapEveryErrorType_ToAValidStatus(/* US-19 AC-1 — guards a future ErrorType addition */)
    {
        foreach (ErrorType errorType in Enum.GetValues<ErrorType>())
        {
            int status = ErrorStatusMapper.ToStatusCode(errorType);
            status.Should().BeInRange(400, 599, $"ErrorType.{errorType} must map to a client/server error status");
        }
    }
}
