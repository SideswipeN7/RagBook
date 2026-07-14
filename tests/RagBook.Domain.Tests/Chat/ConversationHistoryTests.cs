using FluentAssertions;
using RagBook.Modules.Chat.Domain;
using Xunit;

namespace RagBook.Domain.Tests.Chat;

/// <summary>
/// Unit tests for <see cref="ConversationHistory.SelectRecent"/> (US-18): the last N complete user→assistant
/// pairs, in order; a trailing lone user message (the in-flight current question) is excluded; the bound holds.
/// </summary>
public sealed class ConversationHistoryTests
{
    private static readonly Guid Conv = Guid.NewGuid();
    private static DateTimeOffset _clock = new(2026, 7, 13, 12, 0, 0, TimeSpan.Zero);

    private static Message User(string text)
    {
        Message message = Message.User(Conv, text);
        message.CreatedAt = _clock;
        _clock = _clock.AddSeconds(1);

        return message;
    }

    private static Message Assistant(string text)
    {
        Message message = Message.Assistant(Conv, text, MessageState.Answered, sourcesJson: null);
        message.CreatedAt = _clock;
        _clock = _clock.AddSeconds(1);

        return message;
    }

    [Fact]
    public void Should_ReturnLastNPairs_InChronologicalOrder()
    {
        // Arrange — three closed turns.
        var messages = new[]
        {
            User("q1"), Assistant("a1"),
            User("q2"), Assistant("a2"),
            User("q3"), Assistant("a3"),
        };

        // Act — keep only the last 2 pairs.
        IReadOnlyList<Message> history = ConversationHistory.SelectRecent(messages, pairs: 2);

        // Assert
        history.Select(message => message.Content).Should().Equal("q2", "a2", "q3", "a3");
    }

    [Fact]
    public void Should_ExcludeTrailingLoneUserMessage()
    {
        // Arrange — a closed pair, then the current in-flight question (no answer yet).
        var messages = new[] { User("q1"), Assistant("a1"), User("q2-in-flight") };

        // Act
        IReadOnlyList<Message> history = ConversationHistory.SelectRecent(messages, pairs: 6);

        // Assert — only the closed pair.
        history.Select(message => message.Content).Should().Equal("q1", "a1");
    }

    [Fact]
    public void Should_ReturnAllPairs_WhenFewerThanN()
    {
        // Arrange
        var messages = new[] { User("q1"), Assistant("a1") };

        // Act
        IReadOnlyList<Message> history = ConversationHistory.SelectRecent(messages, pairs: 6);

        // Assert
        history.Should().HaveCount(2);
    }

    [Fact]
    public void Should_ReturnEmpty_WhenPairsNonPositive()
    {
        // Arrange
        var messages = new[] { User("q1"), Assistant("a1") };

        // Act + Assert
        ConversationHistory.SelectRecent(messages, pairs: 0).Should().BeEmpty();
    }
}
