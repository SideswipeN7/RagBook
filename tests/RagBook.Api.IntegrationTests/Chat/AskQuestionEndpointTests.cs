using System.Net;
using System.Text.Json;
using FluentAssertions;
using RagBook.Modules.Chat.Domain;
using RagBook.Modules.Folders.Domain;
using Xunit;

namespace RagBook.Api.IntegrationTests.Chat;

/// <summary>
/// Acceptance tests for `POST /api/chat/ask` (US-14) against the real host + pgvector, with the scriptable
/// fake generator. The deterministic embedding fake makes a question that repeats a chunk's text match it
/// (distance ~0), and a different question fall below the threshold — so grounding is deterministic.
/// </summary>
public sealed class AskQuestionEndpointTests(ChatAskApiFactory factory) : IClassFixture<ChatAskApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Should_StreamSourcesThenTokensThenDone_When_Answerable()
    {
        // Arrange — a ready document whose chunk text the question repeats (⇒ high similarity).
        factory.Generator.Reset();
        var session = Guid.NewGuid();
        factory.StoreKey(session);
        await ChatRetrievalSeed.SeedReadyDocumentAsync(factory, session, "umowa.pdf", null, [("okres wypowiedzenia wynosi trzy miesiace", 2)]);
        HttpClient client = SseEvents.CreateClient(factory, session);

        // Act
        HttpResponseMessage response = await SseEvents.AskAsync(client, "okres wypowiedzenia wynosi trzy miesiace", "all");
        IReadOnlyList<SseEvents.Event> events = await SseEvents.ReadAsync(response);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/event-stream");
        events[0].Name.Should().Be("sources");
        events.Count(e => e.Name == "token").Should().BeGreaterThanOrEqualTo(2); // incremental, not one block (A1/SC-004)
        events[^1].Name.Should().Be("done");
        events[^1].Data.Should().Contain("\"groundsFound\":true");
        events[^1].Data.Should().Contain("\"state\":\"answered\""); // US-17 — a produced answer
    }

    [Fact]
    public async Task Should_ReturnNoAnswerState_When_ModelReturnsRefusalSentinel()
    {
        // Arrange (US-17) — passages clear the threshold, but the model refuses with the exact sentinel.
        factory.Generator.Reset();
        factory.Generator.Deltas = [GroundingPrompt.RefusalPhrase];
        var session = Guid.NewGuid();
        factory.StoreKey(session);
        await ChatRetrievalSeed.SeedReadyDocumentAsync(factory, session, "umowa.pdf", null, [("okres wypowiedzenia wynosi trzy miesiace", 2)]);
        HttpClient client = SseEvents.CreateClient(factory, session);

        // Act
        IReadOnlyList<SseEvents.Event> events = await SseEvents.ReadAsync(await SseEvents.AskAsync(client, "okres wypowiedzenia wynosi trzy miesiace", "all"));

        // Assert — prompt-refusal path: grounds existed (a sources event) but the state is no_answer.
        events.Should().Contain(e => e.Name == "sources");
        events[^1].Name.Should().Be("done");
        events[^1].Data.Should().Contain("\"state\":\"no_answer\"");
    }

    [Fact]
    public async Task Should_ReturnAnsweredState_When_SentinelAppearsMidText()
    {
        // Arrange (US-17) — the sentinel is embedded in a longer answer ⇒ a normal answer, not a refusal.
        factory.Generator.Reset();
        factory.Generator.Deltas = ["Umowa nie zawiera tej kary [1]. ", GroundingPrompt.RefusalPhrase];
        var session = Guid.NewGuid();
        factory.StoreKey(session);
        await ChatRetrievalSeed.SeedReadyDocumentAsync(factory, session, "umowa.pdf", null, [("okres wypowiedzenia wynosi trzy miesiace", 2)]);
        HttpClient client = SseEvents.CreateClient(factory, session);

        // Act
        IReadOnlyList<SseEvents.Event> events = await SseEvents.ReadAsync(await SseEvents.AskAsync(client, "okres wypowiedzenia wynosi trzy miesiace", "all"));

        // Assert
        events[^1].Name.Should().Be("done");
        events[^1].Data.Should().Contain("\"state\":\"answered\"");
    }

    [Fact]
    public async Task Should_GroundOnlyOnInScopeSessionPassages()
    {
        // Arrange — same text in-scope, out-of-scope (sibling folder), and in another session.
        factory.Generator.Reset();
        var session = Guid.NewGuid();
        var otherSession = Guid.NewGuid();
        factory.StoreKey(session);
        Folder umowy = await ChatRetrievalSeed.SeedRootFolderAsync(factory, session, "Umowy");
        Folder faktury = await ChatRetrievalSeed.SeedRootFolderAsync(factory, session, "Faktury");
        Guid inScope = await ChatRetrievalSeed.SeedReadyDocumentAsync(factory, session, "in.pdf", umowy.Id, [("wspolna tresc do wyszukania", null)]);
        await ChatRetrievalSeed.SeedReadyDocumentAsync(factory, session, "out.pdf", faktury.Id, [("wspolna tresc do wyszukania", null)]);
        await ChatRetrievalSeed.SeedReadyDocumentAsync(factory, otherSession, "other.pdf", null, [("wspolna tresc do wyszukania", null)]);
        HttpClient client = SseEvents.CreateClient(factory, session);

        // Act
        HttpResponseMessage response = await SseEvents.AskAsync(client, "wspolna tresc do wyszukania", "folder", umowy.Id);
        SseEvents.Event sources = (await SseEvents.ReadAsync(response)).First(e => e.Name == "sources");
        var docIds = JsonSerializer.Deserialize<SourceRow[]>(sources.Data, JsonOptions)!.Select(row => row.DocumentId).ToHashSet();

        // Assert — only the in-scope, current-session document grounds the answer.
        docIds.Should().Contain(inScope);
        docIds.Should().HaveCount(1);
    }

    [Fact]
    public async Task Should_ReturnGroundsFalse_And_NotInvokeGenerator_When_AllBelowThreshold()
    {
        // Arrange — the question is unrelated to the only chunk ⇒ below threshold.
        factory.Generator.Reset();
        var session = Guid.NewGuid();
        factory.StoreKey(session);
        await ChatRetrievalSeed.SeedReadyDocumentAsync(factory, session, "doc.pdf", null, [("zupelnie inny temat o pogodzie", null)]);
        HttpClient client = SseEvents.CreateClient(factory, session);

        // Act
        HttpResponseMessage response = await SseEvents.AskAsync(client, "przepisy o wynagrodzeniu i premiach kwartalnych", "all");
        IReadOnlyList<SseEvents.Event> events = await SseEvents.ReadAsync(response);

        // Assert — deterministic no-grounds: no model call, no sources, a no_answer state (US-17).
        events.Should().ContainSingle(e => e.Name == "done").Which.Data.Should().Contain("\"groundsFound\":false");
        events[^1].Data.Should().Contain("\"state\":\"no_answer\"");
        events.Should().NotContain(e => e.Name == "token");
        events.Should().NotContain(e => e.Name == "sources");
        factory.Generator.Invoked.Should().BeFalse();
    }

    [Fact]
    public async Task Should_ReturnGroundsFalse_When_ScopeAllProcessing()
    {
        // Arrange — the only document is still processing (no ready chunks).
        factory.Generator.Reset();
        var session = Guid.NewGuid();
        factory.StoreKey(session);
        await ChatRetrievalSeed.SeedProcessingDocumentAsync(factory, session, "processing.pdf", null);
        HttpClient client = SseEvents.CreateClient(factory, session);

        // Act
        IReadOnlyList<SseEvents.Event> events = await SseEvents.ReadAsync(await SseEvents.AskAsync(client, "cokolwiek", "all"));

        // Assert
        events.Should().ContainSingle(e => e.Name == "done").Which.Data.Should().Contain("\"groundsFound\":false");
        factory.Generator.Invoked.Should().BeFalse();
    }

    [Fact]
    public async Task Should_IncludeTextAndChunkId_InSources()
    {
        // Arrange (US-16) — the sources event must carry the full chunk text + chunk id for citations.
        factory.Generator.Reset();
        var session = Guid.NewGuid();
        factory.StoreKey(session);
        await ChatRetrievalSeed.SeedReadyDocumentAsync(factory, session, "umowa.pdf", null, [("okres wypowiedzenia wynosi trzy miesiace", 2)]);
        HttpClient client = SseEvents.CreateClient(factory, session);

        // Act
        HttpResponseMessage response = await SseEvents.AskAsync(client, "okres wypowiedzenia wynosi trzy miesiace", "all");
        SseEvents.Event sources = (await SseEvents.ReadAsync(response)).First(e => e.Name == "sources");
        SourceRow row = JsonSerializer.Deserialize<SourceRow[]>(sources.Data, JsonOptions)![0];

        // Assert
        row.Text.Should().Be("okres wypowiedzenia wynosi trzy miesiace");
        row.ChunkId.Should().NotBeEmpty();
    }

    private sealed record SourceRow(int Number, Guid DocumentId, string FileName, int? PageNumber, string Text, Guid ChunkId);
}
