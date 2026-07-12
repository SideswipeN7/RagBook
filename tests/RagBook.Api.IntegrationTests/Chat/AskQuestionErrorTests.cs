using System.Net;
using System.Text.Json;
using FluentAssertions;
using RagBook.Modules.Chat.Domain;
using Xunit;

namespace RagBook.Api.IntegrationTests.Chat;

/// <summary>
/// US-14 AC-5 + guards for `POST /api/chat/ask`: a missing key or invalid question fails before generation
/// (ProblemDetails); a provider failure before the first delta is a ProblemDetails with the mapped code; a
/// failure mid-stream is an SSE `error` event.
/// </summary>
public sealed class AskQuestionErrorTests(ChatAskApiFactory factory) : IClassFixture<ChatAskApiFactory>
{
    private const string Grounded = "unikalna tresc do dopasowania pytania";

    private static async Task<string?> ReadProblemCodeAsync(HttpResponseMessage response)
    {
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return document.RootElement.TryGetProperty("code", out JsonElement code) ? code.GetString() : null;
    }

    [Fact]
    public async Task Should_Return401_When_NoKey()
    {
        // Arrange — no key stored for the session.
        factory.Generator.Reset();
        var session = Guid.NewGuid();
        HttpClient client = SseEvents.CreateClient(factory, session);

        // Act
        HttpResponseMessage response = await SseEvents.AskAsync(client, "cokolwiek", "all");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await ReadProblemCodeAsync(response)).Should().Be("settings.api_key_missing");
        factory.Generator.Invoked.Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Should_Return400_When_QuestionEmpty(string question)
    {
        // Arrange
        factory.Generator.Reset();
        var session = Guid.NewGuid();
        factory.StoreKey(session);
        HttpClient client = SseEvents.CreateClient(factory, session);

        // Act
        HttpResponseMessage response = await SseEvents.AskAsync(client, question, "all");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadProblemCodeAsync(response)).Should().Be("chat.invalid_question");
    }

    [Theory]
    [InlineData(AnswerGenerationFailure.InvalidKey, HttpStatusCode.BadRequest, "settings.invalid_api_key")]
    [InlineData(AnswerGenerationFailure.RateLimited, HttpStatusCode.TooManyRequests, "chat.provider_rate_limited")]
    [InlineData(AnswerGenerationFailure.Unavailable, HttpStatusCode.ServiceUnavailable, "chat.provider_unavailable")]
    public async Task Should_ReturnProblemDetails_When_GeneratorFailsBeforeFirstDelta(
        AnswerGenerationFailure failure,
        HttpStatusCode expectedStatus,
        string expectedCode)
    {
        // Arrange — answerable question, but the generator fails before yielding anything.
        factory.Generator.Reset();
        factory.Generator.FailBeforeFirstDelta = failure;
        var session = Guid.NewGuid();
        factory.StoreKey(session);
        await ChatRetrievalSeed.SeedReadyDocumentAsync(factory, session, "doc.pdf", null, [(Grounded, null)]);
        HttpClient client = SseEvents.CreateClient(factory, session);

        // Act
        HttpResponseMessage response = await SseEvents.AskAsync(client, Grounded, "all");

        // Assert — a normal ProblemDetails (headers not yet sent), not a stream.
        response.StatusCode.Should().Be(expectedStatus);
        (await ReadProblemCodeAsync(response)).Should().Be(expectedCode);
    }

    [Fact]
    public async Task Should_EmitErrorEvent_When_GeneratorFailsMidStream()
    {
        // Arrange — the generator yields one delta then fails.
        factory.Generator.Reset();
        factory.Generator.FailAfterFirstDelta = AnswerGenerationFailure.Unavailable;
        var session = Guid.NewGuid();
        factory.StoreKey(session);
        await ChatRetrievalSeed.SeedReadyDocumentAsync(factory, session, "doc.pdf", null, [(Grounded, null)]);
        HttpClient client = SseEvents.CreateClient(factory, session);

        // Act
        HttpResponseMessage response = await SseEvents.AskAsync(client, Grounded, "all");
        IReadOnlyList<SseEvents.Event> events = await SseEvents.ReadAsync(response);

        // Assert — streaming had begun (sources + a token), then a distinct error event, no clean done.
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        events.Should().Contain(e => e.Name == "sources");
        events.Should().Contain(e => e.Name == "token");
        events.Should().ContainSingle(e => e.Name == "error").Which.Data.Should().Contain("chat.provider_unavailable");
        events.Should().NotContain(e => e.Name == "done");
    }
}
