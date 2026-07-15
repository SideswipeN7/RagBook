using FluentAssertions;
using RagBook.Modules.Chat.Domain;
using Xunit;

namespace RagBook.Domain.Tests.Chat;

/// <summary>
/// Unit tests for <see cref="Conversation"/> (US-18): it starts empty under a scope, titles itself from the
/// first question (truncated, once), and records the latest ask's scope.
/// </summary>
public sealed class ConversationTests
{
    [Fact]
    public void Should_StartEmpty_UnderTheGivenScope()
    {
        // Act
        Conversation conversation = Conversation.Start(ChatScope.All());

        // Assert
        conversation.Title.Should().BeEmpty();
        conversation.Scope.Type.Should().Be(ChatScopeType.All);
    }

    [Fact]
    public void Should_SetTitle_TrimmedAndTruncated_FromFirstQuestion()
    {
        // Arrange
        Conversation conversation = Conversation.Start(ChatScope.All());
        var question = $"  {new string('a', 100)}  ";

        // Act
        conversation.SetTitleFromFirstQuestion(question, maxChars: 60);

        // Assert
        conversation.Title.Should().Be(new string('a', 60));
    }

    [Fact]
    public void Should_KeepShortTitleAsIs()
    {
        // Arrange
        Conversation conversation = Conversation.Start(ChatScope.All());

        // Act
        conversation.SetTitleFromFirstQuestion("Jaki jest okres wypowiedzenia?", maxChars: 60);

        // Assert
        conversation.Title.Should().Be("Jaki jest okres wypowiedzenia?");
    }

    [Fact]
    public void Should_NotRewriteTitle_OnLaterQuestions()
    {
        // Arrange
        Conversation conversation = Conversation.Start(ChatScope.All());
        conversation.SetTitleFromFirstQuestion("Pierwsze pytanie", maxChars: 60);

        // Act
        conversation.SetTitleFromFirstQuestion("Drugie pytanie", maxChars: 60);

        // Assert
        conversation.Title.Should().Be("Pierwsze pytanie");
    }

    [Fact]
    public void Should_RecordLatestScope_OnUpdate()
    {
        // Arrange
        Conversation conversation = Conversation.Start(ChatScope.All());
        var folderId = Guid.NewGuid();

        // Act
        conversation.UpdateScope(ChatScope.Folder(folderId));

        // Assert
        conversation.Scope.Type.Should().Be(ChatScopeType.Folder);
        conversation.Scope.TargetId.Should().Be(folderId);
    }
}
