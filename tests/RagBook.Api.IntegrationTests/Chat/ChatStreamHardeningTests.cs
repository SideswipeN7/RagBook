using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Xunit;

namespace RagBook.Api.IntegrationTests.Chat;

/// <summary>
/// US-15 backend hardening over the US-14 stream: the generation is **cancelled** when the client
/// disconnects mid-stream (FR-004/AC-5), and a **keep-alive** comment is emitted during a slow stream so an
/// intermediary idle-timeout does not cut it (FR-010). Uses the delaying fake generator (no real Anthropic).
/// </summary>
public sealed class ChatStreamHardeningTests(ChatAskApiFactory factory) : IClassFixture<ChatAskApiFactory>
{
    private const string Grounded = "unikalna tresc do dopasowania pytania w hardeningu";

    private async Task SeedAnswerableAsync(Guid session)
    {
        factory.StoreKey(session);
        await ChatRetrievalSeed.SeedReadyDocumentAsync(factory, session, "doc.pdf", null, [(Grounded, null)]);
    }

    private static HttpRequestMessage AskRequest(Guid conversationId)
    {
        return new HttpRequestMessage(HttpMethod.Post, "/api/chat/ask")
        {
            Content = JsonContent.Create(new { conversationId, question = Grounded, scope = new { type = "all" } }),
        };
    }

    [Fact]
    public async Task Should_CancelGeneration_When_ClientDisconnectsMidStream()
    {
        // Arrange — a stream that stalls between deltas so we can abort while it is generating.
        factory.Generator.Reset();
        factory.Generator.DelayPerDelta = TimeSpan.FromSeconds(5);
        var session = Guid.NewGuid();
        await SeedAnswerableAsync(session);
        HttpClient client = SseEvents.CreateClient(factory, session);
        Guid conversation = await SseEvents.CreateConversationAsync(client);

        // Act — start reading, then disconnect the client mid-stream (dispose the response/stream + cancel).
        // The in-memory TestServer surfaces the abandoned response as HttpContext.RequestAborted.
        var abort = new CancellationTokenSource();
        HttpResponseMessage response = await client.SendAsync(AskRequest(conversation), HttpCompletionOption.ResponseHeadersRead, abort.Token);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        Stream stream = await response.Content.ReadAsStreamAsync(abort.Token);
        _ = await stream.ReadAsync(new byte[128], abort.Token); // let `sources` + the first `token` flow
        await abort.CancelAsync();
        stream.Dispose();
        response.Dispose();
        client.Dispose();

        // Assert — the server observes cancellation of the in-flight generation.
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (!factory.Generator.CancellationObserved && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(50, CancellationToken.None);
        }

        factory.Generator.CancellationObserved.Should().BeTrue();
    }

    [Fact]
    public async Task Should_EmitKeepAliveComment_When_GenerationIsSlow()
    {
        // Arrange — delay per delta (1.5s) exceeds the test host's 1s heartbeat interval.
        factory.Generator.Reset();
        factory.Generator.DelayPerDelta = TimeSpan.FromSeconds(1.5);
        var session = Guid.NewGuid();
        await SeedAnswerableAsync(session);
        HttpClient client = SseEvents.CreateClient(factory, session);
        Guid conversation = await SseEvents.CreateConversationAsync(client);

        // Act — read the whole stream to completion.
        HttpResponseMessage response = await client.SendAsync(AskRequest(conversation), CancellationToken.None);
        string body = await response.Content.ReadAsStringAsync(CancellationToken.None);

        // Assert — a keep-alive comment appeared, and the stream still completed normally.
        body.Should().Contain(": keep-alive");
        body.Should().Contain("event: done");
    }
}
