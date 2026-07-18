using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace RagBook.Api.IntegrationTests.ErrorHandling;

/// <summary>
/// Acceptance tests for US-19 error handling against the real host: an unhandled exception becomes a sanitized 500
/// ProblemDetails (AC-5), every error response carries an <c>X-Trace-Id</c> header equal to the body <c>traceId</c>,
/// and that same id appears in the server logs (AC-4).
/// </summary>
public sealed class ErrorHandlingTests(ErrorHandlingApiFactory factory) : IClassFixture<ErrorHandlingApiFactory>
{
    [Fact]
    public async Task Should_Return500ProblemDetails_WithNoStackTrace_When_HandlerThrows()
    {
        // Arrange (AC-5)
        HttpClient client = factory.CreateSessionClient();

        // Act
        HttpResponseMessage response = await client.GetAsync("/api/_test/throw");
        string body = await response.Content.ReadAsStringAsync();

        // Assert — sanitized 500 ProblemDetails, stable code, and NO stack trace / exception type leaked.
        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        using JsonDocument problem = JsonDocument.Parse(body);
        problem.RootElement.GetProperty("code").GetString().Should().Be("error.unexpected");
        problem.RootElement.GetProperty("status").GetInt32().Should().Be(500);
        body.Should().NotContain("InvalidOperationException").And.NotContain("at RagBook").And.NotContain("stackTrace");
    }

    [Fact]
    public async Task Should_CarryXTraceIdHeader_EqualToBodyTraceId_OnAnErrorResponse()
    {
        // Arrange (AC-4)
        HttpClient client = factory.CreateSessionClient();

        // Act
        HttpResponseMessage response = await client.GetAsync("/api/_test/throw");
        string body = await response.Content.ReadAsStringAsync();

        // Assert — the header is present and equals the ProblemDetails traceId.
        response.Headers.TryGetValues("X-Trace-Id", out IEnumerable<string>? headerValues).Should().BeTrue();
        string headerTraceId = headerValues!.Single();
        headerTraceId.Should().NotBeNullOrWhiteSpace();

        using JsonDocument problem = JsonDocument.Parse(body);
        string bodyTraceId = problem.RootElement.GetProperty("traceId").GetString()!;
        headerTraceId.Should().Be(bodyTraceId);
    }

    [Fact]
    public async Task Should_LogTheSameCorrelationId_ThatItReturns_ForAnUnexpectedError()
    {
        // Arrange (AC-4) — the reported id must be traceable in the logs.
        HttpClient client = factory.CreateSessionClient();

        // Act
        HttpResponseMessage response = await client.GetAsync("/api/_test/throw");
        string traceId = response.Headers.GetValues("X-Trace-Id").Single();

        // Assert — GlobalExceptionHandler logged the unhandled exception with the same correlation id.
        factory.Logs.Messages.Should().Contain(message => message.Contains(traceId));
    }

    [Fact]
    public async Task Should_CarryXTraceIdHeader_OnANotFoundError()
    {
        // Arrange — a domain 404 (a conversation not in the session) must also carry the header (consistency).
        HttpClient client = factory.CreateSessionClient();

        // Act — asking against a non-existent conversation returns a ProblemDetails 404.
        HttpResponseMessage response = await client.PostAsJsonAsync("/api/chat/ask", new
        {
            conversationId = Guid.NewGuid(),
            question = "test",
            scope = new { type = "all", targetId = (Guid?)null },
        });

        // Assert — some error status with the X-Trace-Id header present (the middleware stamps every response).
        ((int)response.StatusCode).Should().BeGreaterThanOrEqualTo(400);
        response.Headers.Contains("X-Trace-Id").Should().BeTrue();
    }
}
