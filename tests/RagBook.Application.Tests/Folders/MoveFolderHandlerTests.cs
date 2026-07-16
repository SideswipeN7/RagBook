using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using RagBook.Modules.Folders;
using RagBook.Modules.Folders.Domain;
using RagBook.Modules.Folders.Features.MoveFolder;
using RagBook.Shared.Results;
using Xunit;

namespace RagBook.Application.Tests.Folders;

/// <summary>
/// Unit tests for <see cref="MoveFolderCommandHandler"/> (US-11): ownership, cycle, depth, and duplicate-name
/// validation, the same-parent no-op, and the successful move.
/// </summary>
public sealed class MoveFolderHandlerTests
{
    private static readonly FolderNameRules Rules = new FolderOptions().ToNameRules();
    private readonly IFolderMoveRepository _repository = Substitute.For<IFolderMoveRepository>();

    private MoveFolderCommandHandler CreateSut(int maxDepth = 3)
    {
        return new MoveFolderCommandHandler(_repository, Options.Create(new FolderOptions { MaxDepth = maxDepth }));
    }

    private static Folder Root(string name)
    {
        return Folder.CreateRoot(name, Rules).Value;
    }

    private static Folder Child(Folder parent, string name)
    {
        return Folder.CreateChild(parent, name, Rules, maxDepth: 3).Value;
    }

    private void DepthReturns(int value)
    {
        _repository.MaxSubtreeDepthAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(value);
    }

    [Fact]
    public async Task Should_ReturnNotFound_When_FolderAbsent()
    {
        _repository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Folder?)null);

        Result result = await CreateSut().Handle(new MoveFolderCommand(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        result.Error.Code.Should().Be("folder.not_found");
    }

    [Fact]
    public async Task Should_NoOp_When_MovingToCurrentParent()
    {
        // Arrange — a root folder moved to the root (its current parent = null).
        Folder moved = Root("A");
        _repository.GetByIdAsync(moved.Id, Arg.Any<CancellationToken>()).Returns(moved);

        // Act
        Result result = await CreateSut().Handle(new MoveFolderCommand(moved.Id, null), CancellationToken.None);

        // Assert — success, no write.
        result.IsSuccess.Should().BeTrue();
        await _repository.DidNotReceive().MoveAsync(Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_ReturnCircularMove_When_TargetIsADescendant()
    {
        // Arrange — A and A/B; move A into B.
        Folder a = Root("A");
        Folder b = Child(a, "B");
        _repository.GetByIdAsync(a.Id, Arg.Any<CancellationToken>()).Returns(a);
        _repository.GetByIdAsync(b.Id, Arg.Any<CancellationToken>()).Returns(b);

        // Act
        Result result = await CreateSut().Handle(new MoveFolderCommand(a.Id, b.Id), CancellationToken.None);

        // Assert
        result.Error.Code.Should().Be("folder.circular_move");
    }

    [Fact]
    public async Task Should_ReturnMaxDepthExceeded_When_ResultTooDeep()
    {
        // Arrange — moved is depth 2 with a descendant at depth 3; target is depth 2 ⇒ result depth 4 (> 3).
        Folder moved = Child(Root("R"), "M");
        Folder target = Child(Root("T"), "X");
        _repository.GetByIdAsync(moved.Id, Arg.Any<CancellationToken>()).Returns(moved);
        _repository.GetByIdAsync(target.Id, Arg.Any<CancellationToken>()).Returns(target);
        DepthReturns(3); // deepest descendant of the moved subtree

        // Act
        Result result = await CreateSut().Handle(new MoveFolderCommand(moved.Id, target.Id), CancellationToken.None);

        // Assert
        result.Error.Code.Should().Be("folder.max_depth_exceeded");
    }

    [Fact]
    public async Task Should_ReturnDuplicateName_When_TargetHasSameNamedChild()
    {
        // Arrange
        Folder moved = Root("A");
        Folder target = Root("T");
        _repository.GetByIdAsync(moved.Id, Arg.Any<CancellationToken>()).Returns(moved);
        _repository.GetByIdAsync(target.Id, Arg.Any<CancellationToken>()).Returns(target);
        DepthReturns(1);
        _repository.SiblingExistsAsync(target.Id, "A", moved.Id, Arg.Any<CancellationToken>()).Returns(true);

        // Act
        Result result = await CreateSut().Handle(new MoveFolderCommand(moved.Id, target.Id), CancellationToken.None);

        // Assert
        result.Error.Code.Should().Be("folder.duplicate_name");
    }

    [Fact]
    public async Task Should_MoveAndPersist_When_Valid()
    {
        // Arrange
        Folder moved = Root("A");
        Folder target = Root("T");
        _repository.GetByIdAsync(moved.Id, Arg.Any<CancellationToken>()).Returns(moved);
        _repository.GetByIdAsync(target.Id, Arg.Any<CancellationToken>()).Returns(target);
        DepthReturns(1);
        _repository.SiblingExistsAsync(Arg.Any<Guid?>(), Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(false);

        // Act
        Result result = await CreateSut().Handle(new MoveFolderCommand(moved.Id, target.Id), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        await _repository.Received(1).MoveAsync(moved.Id, target.Id, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
