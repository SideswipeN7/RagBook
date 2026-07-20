using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Xunit;

namespace RagBook.Api.IntegrationTests.Chat;

/// <summary>
/// End-to-end acceptance for keyless CLI mode (US-22): with <c>ClaudeCli:Enabled=true</c>, <c>GET /api/config</c>
/// advertises keyless generation, and an ask with <b>no</b> session key is no longer blocked with
/// <c>settings.api_key_missing</c> (401) — it reaches the pipeline and streams. The scriptable fake generator
/// stands in for the real CLI, so no process is launched.
/// </summary>
public sealed class KeylessCliAskTests(KeylessCliAskFactory factory) : IClassFixture<KeylessCliAskFactory>
{
    [Fact]
    public async Task Should_AdvertiseKeylessGeneration_When_CliEnabled()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/config");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        AppConfig? config = await response.Content.ReadFromJsonAsync<AppConfig>();
        config!.KeylessGeneration.Should().BeTrue();
    }

    [Fact]
    public async Task Should_NotReturn401_And_Stream_When_NoKey_But_CliEnabled()
    {
        // Arrange — a ready document the question repeats (⇒ grounds found), and NO stored key.
        factory.Generator.Reset();
        var session = Guid.NewGuid();
        await ChatRetrievalSeed.SeedReadyDocumentAsync(
            factory, session, "umowa.pdf", null, [("okres wypowiedzenia wynosi trzy miesiace", 2)]);
        HttpClient client = SseEvents.CreateClient(factory, session);

        // Act — ask with no BYOK key; keyless CLI mode must let it through the key guard.
        HttpResponseMessage response = await SseEvents.AskAsync(
            client, "okres wypowiedzenia wynosi trzy miesiace", "all");
        IReadOnlyList<SseEvents.Event> events = await SseEvents.ReadAsync(response);

        // Assert — not the key-missing 401; the pipeline streamed the grounded answer.
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/event-stream");
        events.Should().Contain(e => e.Name == "sources");
        events[^1].Name.Should().Be("done");
        events[^1].Data.Should().Contain("\"state\":\"answered\"");
    }

    private sealed record AppConfig(bool KeylessGeneration);
}
