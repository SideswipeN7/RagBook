using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using RagBook.Modules.Chat.Domain;
using Xunit;

namespace RagBook.Api.IntegrationTests.Chat;

/// <summary>
/// US-18 persistence + multi-turn acceptance over the real host (fake generator). An ask persists the user
/// question and — via the durable outbox handler (in-process here) — the assistant message with its state +
/// sources; a follow-up carries the prior turn into the prompt; the state/citations reload; a new ask whose scope
/// names a missing folder fails as scope-not-found (FR-009).
/// </summary>
public sealed class ConversationPersistenceTests(ChatAskApiFactory factory) : IClassFixture<ChatAskApiFactory>
{
    private static readonly JsonSerializerOptions JsonWeb = new(JsonSerializerDefaults.Web);
    private const string Grounded = "okres wypowiedzenia umowy wynosi trzy miesiace";

    private sealed record ConversationDetailDto(Guid Id, string Title, string ScopeType, Guid? ScopeTargetId, DateTimeOffset CreatedAt, List<MessageDto> Messages);
    private sealed record MessageDto(Guid Id, string Role, string Content, string? State, List<SourceRow>? Sources, DateTimeOffset CreatedAt);
    private sealed record SourceRow(int Number, Guid DocumentId, string FileName, int? PageNumber, string Text, Guid ChunkId);

    private static async Task<ConversationDetailDto> LoadUntilAsync(HttpClient client, Guid id, Func<ConversationDetailDto, bool> until)
    {
        for (int attempt = 0; attempt < 50; attempt++)
        {
            ConversationDetailDto? detail = await client.GetFromJsonAsync<ConversationDetailDto>($"/api/conversations/{id}", JsonWeb);
            if (detail is not null && until(detail))
            {
                return detail;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException("The conversation did not reach the expected state in time.");
    }

    [Fact]
    public async Task Should_PersistUserAndAssistantMessages_WithStateAndSources()
    {
        // Arrange
        factory.Generator.Reset();
        var session = Guid.NewGuid();
        factory.StoreKey(session);
        await ChatRetrievalSeed.SeedReadyDocumentAsync(factory, session, "umowa.pdf", null, [(Grounded, 2)]);
        HttpClient client = SseEvents.CreateClient(factory, session);
        Guid conversation = await SseEvents.CreateConversationAsync(client);

        // Act
        await SseEvents.ReadAsync(await SseEvents.AskInAsync(client, conversation, Grounded, "all"));
        ConversationDetailDto detail = await LoadUntilAsync(client, conversation, loaded => loaded.Messages.Count >= 2);

        // Assert — the user question and the assistant answer (with state + sources) are persisted; title set.
        detail.Title.Should().Be(Grounded);
        detail.Messages[0].Role.Should().Be("user");
        detail.Messages[0].Content.Should().Be(Grounded);
        MessageDto assistant = detail.Messages[1];
        assistant.Role.Should().Be("assistant");
        assistant.State.Should().Be("answered");
        assistant.Sources.Should().NotBeNullOrEmpty();
        assistant.Sources![0].Text.Should().Be(Grounded);
    }

    [Fact]
    public async Task Should_IncludePriorTurn_InFollowUpPrompt()
    {
        // Arrange — a first, answered turn.
        factory.Generator.Reset();
        var session = Guid.NewGuid();
        factory.StoreKey(session);
        await ChatRetrievalSeed.SeedReadyDocumentAsync(factory, session, "umowa.pdf", null, [(Grounded, 2)]);
        HttpClient client = SseEvents.CreateClient(factory, session);
        Guid conversation = await SseEvents.CreateConversationAsync(client);
        await SseEvents.ReadAsync(await SseEvents.AskInAsync(client, conversation, Grounded, "all"));
        await LoadUntilAsync(client, conversation, loaded => loaded.Messages.Count >= 2);

        // Act — a follow-up in the same conversation.
        await SseEvents.ReadAsync(await SseEvents.AskInAsync(client, conversation, "a co z okresem?", "all"));

        // Assert — the prompt the generator saw carried the prior question as conversational context.
        factory.Generator.LastContext.Should().NotBeNull();
        factory.Generator.LastContext!.UserPrompt.Should().Contain("Wcześniejsza rozmowa:");
        factory.Generator.LastContext!.UserPrompt.Should().Contain(Grounded);
    }

    [Fact]
    public async Task Should_PersistNoAnswerState_When_ModelRefuses()
    {
        // Arrange — passages clear the threshold, but the model returns the refusal sentinel (US-17).
        factory.Generator.Reset();
        factory.Generator.Deltas = [GroundingPrompt.RefusalPhrase];
        var session = Guid.NewGuid();
        factory.StoreKey(session);
        await ChatRetrievalSeed.SeedReadyDocumentAsync(factory, session, "umowa.pdf", null, [(Grounded, 2)]);
        HttpClient client = SseEvents.CreateClient(factory, session);
        Guid conversation = await SseEvents.CreateConversationAsync(client);

        // Act
        await SseEvents.ReadAsync(await SseEvents.AskInAsync(client, conversation, Grounded, "all"));
        ConversationDetailDto detail = await LoadUntilAsync(client, conversation, loaded => loaded.Messages.Count >= 2);

        // Assert
        detail.Messages[1].State.Should().Be("no_answer");
    }

    [Fact]
    public async Task Should_FailNewAsk_When_ScopeTargetsMissingFolder()
    {
        // Arrange (FR-009) — an ask whose folder scope names a non-existent folder.
        factory.Generator.Reset();
        var session = Guid.NewGuid();
        factory.StoreKey(session);
        HttpClient client = SseEvents.CreateClient(factory, session);
        Guid conversation = await SseEvents.CreateConversationAsync(client);

        // Act
        HttpResponseMessage response = await SseEvents.AskInAsync(client, conversation, "cokolwiek", "folder", Guid.NewGuid());

        // Assert — the scope no longer resolves → scope-not-found (US-13), never a crash.
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).Should().Contain("chat.scope_not_found");
    }
}
