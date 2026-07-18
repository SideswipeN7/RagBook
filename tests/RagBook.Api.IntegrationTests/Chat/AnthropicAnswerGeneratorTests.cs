using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Options;
using RagBook.Infrastructure.SharedContext.Providers.Anthropic;
using RagBook.Modules.Chat.Domain;
using RagBook.Modules.Settings.Domain;
using RagBook.Shared.Results;
using Xunit;

namespace RagBook.Api.IntegrationTests.Chat;

/// <summary>
/// Unit tests for <see cref="AnthropicAnswerGenerator"/> (US-14) — the SSE parse + error mapping — driven by a
/// canned <see cref="HttpMessageHandler"/>, so no test hits the real provider (§V). A 200 stream yields its
/// text deltas in order; a non-2xx status or a thrown transport error maps to the right failure.
/// </summary>
public sealed class AnthropicAnswerGeneratorTests
{
    private const string StreamBody =
        "event: content_block_delta\n" +
        "data: {\"type\":\"content_block_delta\",\"index\":0,\"delta\":{\"type\":\"text_delta\",\"text\":\"Hello\"}}\n\n" +
        "event: content_block_delta\n" +
        "data: {\"type\":\"content_block_delta\",\"index\":0,\"delta\":{\"type\":\"text_delta\",\"text\":\" world\"}}\n\n" +
        "event: message_stop\n" +
        "data: {\"type\":\"message_stop\"}\n\n";

    private static AnthropicAnswerGenerator CreateSut(StubHttpMessageHandler handler)
    {
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.anthropic.test") };

        return new AnthropicAnswerGenerator(
            new SingleClientFactory(client),
            new StubClientFactory(),
            Options.Create(new AnthropicOptions()));
    }

    private static GroundedContext Context()
    {
        return new GroundedContext([], "system", "user");
    }

    [Fact]
    public async Task Should_YieldDeltas_InOrder_When_200Stream()
    {
        var sut = CreateSut(StubHttpMessageHandler.Streaming(HttpStatusCode.OK, StreamBody));

        var deltas = new List<string>();
        await foreach (string delta in sut.GenerateAsync(Context(), CancellationToken.None))
        {
            deltas.Add(delta);
        }

        deltas.Should().Equal("Hello", " world");
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, AnswerGenerationFailure.InvalidKey)]
    [InlineData(HttpStatusCode.Forbidden, AnswerGenerationFailure.InvalidKey)]
    [InlineData(HttpStatusCode.TooManyRequests, AnswerGenerationFailure.RateLimited)]
    [InlineData(HttpStatusCode.InternalServerError, AnswerGenerationFailure.Unavailable)]
    public async Task Should_Throw_MappedFailure_When_NonSuccessStatus(HttpStatusCode status, AnswerGenerationFailure expected)
    {
        var sut = CreateSut(StubHttpMessageHandler.Streaming(status, string.Empty));

        Func<Task> act = async () =>
        {
            await foreach (string _ in sut.GenerateAsync(Context(), CancellationToken.None))
            {
            }
        };

        (await act.Should().ThrowAsync<AnswerGenerationException>()).Which.Failure.Should().Be(expected);
    }

    [Fact]
    public async Task Should_ThrowUnavailable_When_TransportThrows()
    {
        var sut = CreateSut(StubHttpMessageHandler.Throwing(new HttpRequestException("boom")));

        Func<Task> act = async () =>
        {
            await foreach (string _ in sut.GenerateAsync(Context(), CancellationToken.None))
            {
            }
        };

        (await act.Should().ThrowAsync<AnswerGenerationException>()).Which.Failure.Should().Be(AnswerGenerationFailure.Unavailable);
    }

    private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            return client;
        }
    }

    private sealed class StubClientFactory : IAnthropicClientFactory
    {
        public Result<AnthropicClientHandle> CreateForSession()
        {
            return new AnthropicClientHandle("sk-ant-api03-test");
        }

        public Result<AnthropicClientHandle> CreateForDemo()
        {
            return new AnthropicClientHandle("sk-ant-api03-appkey");
        }
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode? _status;
        private readonly string? _body;
        private readonly Exception? _exception;

        private StubHttpMessageHandler(HttpStatusCode? status, string? body, Exception? exception)
        {
            _status = status;
            _body = body;
            _exception = exception;
        }

        public static StubHttpMessageHandler Streaming(HttpStatusCode status, string body)
        {
            return new StubHttpMessageHandler(status, body, null);
        }

        public static StubHttpMessageHandler Throwing(Exception exception)
        {
            return new StubHttpMessageHandler(null, null, exception);
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (_exception is not null)
            {
                throw _exception;
            }

            return Task.FromResult(new HttpResponseMessage(_status!.Value)
            {
                Content = new StringContent(_body ?? string.Empty, Encoding.UTF8, "text/event-stream"),
            });
        }
    }
}
