using System.Net;
using FluentAssertions;
using Xunit;

namespace RagBook.Api.IntegrationTests.Chat;

/// <summary>
/// US-18 AC-5 — conversations are session-isolated. A conversation created in one session is invisible to
/// another: getting or deleting its id from a different session returns 404 (never disclosing existence),
/// consistent with US-01.
/// </summary>
public sealed class ConversationIsolationTests(ChatAskApiFactory factory) : IClassFixture<ChatAskApiFactory>
{
    [Fact]
    public async Task Should_Return404_When_AccessingAnotherSessionsConversation()
    {
        // Arrange — session A owns a conversation.
        var sessionA = Guid.NewGuid();
        var sessionB = Guid.NewGuid();
        factory.StoreKey(sessionA);
        HttpClient clientA = SseEvents.CreateClient(factory, sessionA);
        Guid conversation = await SseEvents.CreateConversationAsync(clientA);

        HttpClient clientB = SseEvents.CreateClient(factory, sessionB);

        // Act
        HttpResponseMessage get = await clientB.GetAsync($"/api/conversations/{conversation}");
        HttpResponseMessage delete = await clientB.DeleteAsync($"/api/conversations/{conversation}");

        // Assert — session B cannot see or delete session A's conversation.
        get.StatusCode.Should().Be(HttpStatusCode.NotFound);
        delete.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
