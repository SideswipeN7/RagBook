using FluentAssertions;
using NSubstitute;
using RagBook.Modules.Chat.Domain;
using RagBook.Modules.Chat.Features.Conversations;
using RagBook.Modules.Chat.Features.Conversations.CreateConversation;
using Xunit;

namespace RagBook.Application.Tests.Chat;

/// <summary>
/// Unit tests for <see cref="CreateConversationCommandHandler"/> (US-18): it starts an empty conversation under
/// the requested scope (falling back to <c>All</c>) and persists it via the repository.
/// </summary>
public sealed class CreateConversationHandlerTests
{
    private readonly IConversationRepository _repository = Substitute.For<IConversationRepository>();

    private CreateConversationCommandHandler CreateSut()
    {
        return new CreateConversationCommandHandler(_repository);
    }

    [Fact]
    public async Task Should_CreateEmptyConversation_WithDefaultAllScope()
    {
        // Arrange
        var command = new CreateConversationCommand(ChatScopeType.All, ScopeTargetId: null);

        // Act
        ConversationSummary summary = await CreateSut().Handle(command, CancellationToken.None);

        // Assert
        summary.Title.Should().BeEmpty();
        summary.ScopeType.Should().Be("all");
        summary.ScopeTargetId.Should().BeNull();
        await _repository.Received(1).AddAsync(Arg.Is<Conversation>(conversation => conversation.ScopeType == ChatScopeType.All), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_CreateConversation_UnderAFolderScope()
    {
        // Arrange
        var folderId = Guid.NewGuid();
        var command = new CreateConversationCommand(ChatScopeType.Folder, folderId);

        // Act
        ConversationSummary summary = await CreateSut().Handle(command, CancellationToken.None);

        // Assert
        summary.ScopeType.Should().Be("folder");
        summary.ScopeTargetId.Should().Be(folderId);
    }

    [Fact]
    public async Task Should_FallBackToAll_When_FolderScopeHasNoTarget()
    {
        // Arrange — a folder scope without a target id is not representable; the handler falls back to All.
        var command = new CreateConversationCommand(ChatScopeType.Folder, ScopeTargetId: null);

        // Act
        ConversationSummary summary = await CreateSut().Handle(command, CancellationToken.None);

        // Assert
        summary.ScopeType.Should().Be("all");
    }
}
