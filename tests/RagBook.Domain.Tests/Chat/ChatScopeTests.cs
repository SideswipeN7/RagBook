using FluentAssertions;
using RagBook.Modules.Chat.Domain;
using Xunit;

namespace RagBook.Domain.Tests.Chat;

/// <summary>
/// Unit tests for <see cref="ChatScope"/> (US-13). The factories are the only construction path, so an
/// invalid combination (All with a target, or folder/document without one) is unrepresentable.
/// </summary>
public sealed class ChatScopeTests
{
    [Fact]
    public void Should_HaveNoTarget_When_All()
    {
        // Act
        ChatScope scope = ChatScope.All();

        // Assert
        scope.Type.Should().Be(ChatScopeType.All);
        scope.TargetId.Should().BeNull();
    }

    [Fact]
    public void Should_CarryFolderId_When_Folder()
    {
        // Arrange
        var folderId = Guid.NewGuid();

        // Act
        ChatScope scope = ChatScope.Folder(folderId);

        // Assert
        scope.Type.Should().Be(ChatScopeType.Folder);
        scope.TargetId.Should().Be(folderId);
    }

    [Fact]
    public void Should_CarryDocumentId_When_Document()
    {
        // Arrange
        var documentId = Guid.NewGuid();

        // Act
        ChatScope scope = ChatScope.Document(documentId);

        // Assert
        scope.Type.Should().Be(ChatScopeType.Document);
        scope.TargetId.Should().Be(documentId);
    }
}
