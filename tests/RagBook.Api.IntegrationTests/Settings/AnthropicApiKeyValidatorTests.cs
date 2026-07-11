using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Options;
using RagBook.Infrastructure.SharedContext.Providers.Anthropic;
using RagBook.Infrastructure.SharedContext.Settings;
using RagBook.Modules.Settings.Domain;
using Xunit;

namespace RagBook.Api.IntegrationTests.Settings;

/// <summary>
/// Unit tests for <see cref="AnthropicApiKeyValidator"/> — the status→outcome mapping of the
/// non-generative liveness check (US-02 AC-1). A stub message handler stands in for Anthropic so no test
/// hits the network: <c>200</c>→Valid, <c>401/403</c>→Rejected, and any other status or thrown exception
/// (timeout / circuit-breaker / network) →Unavailable (transient).
/// </summary>
public sealed class AnthropicApiKeyValidatorTests
{
    private static AnthropicApiKeyValidator CreateSut(StubHttpMessageHandler handler)
    {
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.anthropic.test") };

        return new AnthropicApiKeyValidator(
            new SingleClientFactory(client),
            Options.Create(new AnthropicOptions()));
    }

    [Fact]
    public async Task Should_ReturnValid_When_200()
    {
        var sut = CreateSut(StubHttpMessageHandler.Returning(HttpStatusCode.OK));

        (await sut.ValidateAsync("sk-ant-key", CancellationToken.None)).Should().Be(ApiKeyValidationResult.Valid);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task Should_ReturnRejected_When_401Or403(HttpStatusCode status)
    {
        var sut = CreateSut(StubHttpMessageHandler.Returning(status));

        (await sut.ValidateAsync("sk-ant-key", CancellationToken.None)).Should().Be(ApiKeyValidationResult.Rejected);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task Should_ReturnUnavailable_When_ServerErrorOrThrottled(HttpStatusCode status)
    {
        var sut = CreateSut(StubHttpMessageHandler.Returning(status));

        (await sut.ValidateAsync("sk-ant-key", CancellationToken.None)).Should().Be(ApiKeyValidationResult.Unavailable);
    }

    [Fact]
    public async Task Should_ReturnUnavailable_When_TransportThrows()
    {
        var sut = CreateSut(StubHttpMessageHandler.Throwing(new HttpRequestException("boom")));

        (await sut.ValidateAsync("sk-ant-key", CancellationToken.None)).Should().Be(ApiKeyValidationResult.Unavailable);
    }

    [Fact]
    public async Task Should_ReturnUnavailable_When_TimeoutRejected()
    {
        // Resilience pipelines surface a timeout as an exception that is neither HttpRequestException nor a
        // user-cancellation; the validator must still classify it as transient.
        var sut = CreateSut(StubHttpMessageHandler.Throwing(new TimeoutException("attempt timed out")));

        (await sut.ValidateAsync("sk-ant-key", CancellationToken.None)).Should().Be(ApiKeyValidationResult.Unavailable);
    }

    private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            return client;
        }
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode? _status;
        private readonly Exception? _exception;

        private StubHttpMessageHandler(HttpStatusCode? status, Exception? exception)
        {
            _status = status;
            _exception = exception;
        }

        public static StubHttpMessageHandler Returning(HttpStatusCode status)
        {
            return new StubHttpMessageHandler(status, null);
        }

        public static StubHttpMessageHandler Throwing(Exception exception)
        {
            return new StubHttpMessageHandler(null, exception);
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (_exception is not null)
            {
                throw _exception;
            }

            return Task.FromResult(new HttpResponseMessage(_status!.Value));
        }
    }
}
