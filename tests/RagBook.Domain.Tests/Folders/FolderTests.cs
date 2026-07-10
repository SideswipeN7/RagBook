using FluentAssertions;
using RagBook.Modules.Folders.Domain;
using RagBook.Modules.Folders.Errors;
using RagBook.Shared.Results;
using Xunit;

namespace RagBook.Domain.Tests.Folders;

public sealed class FolderTests
{
    private const int MaxDepth = 3;

    private static readonly FolderNameRules Rules = new(MaxNameLength: 100);

    [Fact]
    public void Should_BuildRootPath_When_CreatingRoot()
    {
        // Arrange & Act
        Result<Folder> result = Folder.CreateRoot("Umowy", Rules);

        // Assert (AC-1)
        result.IsSuccess.Should().BeTrue();
        Folder folder = result.Value;
        folder.Name.Should().Be("Umowy");
        folder.ParentId.Should().BeNull();
        folder.Path.Should().Be($"/{folder.Id:N}/");
        folder.UserSessionId.Should().Be(Guid.Empty);
    }

    [Fact]
    public void Should_BuildChildPathUnderParent_When_CreatingChild()
    {
        // Arrange
        Folder parent = Folder.CreateRoot("Umowy", Rules).Value;

        // Act
        Result<Folder> result = Folder.CreateChild(parent, "2026", Rules, MaxDepth);

        // Assert (AC-1)
        result.IsSuccess.Should().BeTrue();
        Folder child = result.Value;
        child.ParentId.Should().Be(parent.Id);
        child.Path.Should().Be($"{parent.Path}{child.Id:N}/");
    }

    [Fact]
    public void Should_ReturnMaxDepthExceeded_When_ParentAtMaxDepth()
    {
        // Arrange — a chain at the maximum allowed depth (root → child → grandchild = depth 3).
        Folder root = Folder.CreateRoot("L1", Rules).Value;
        Folder child = Folder.CreateChild(root, "L2", Rules, MaxDepth).Value;
        Folder grandchild = Folder.CreateChild(child, "L3", Rules, MaxDepth).Value;

        // Act
        Result<Folder> result = Folder.CreateChild(grandchild, "L4", Rules, MaxDepth);

        // Assert (AC-2)
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(FolderErrors.MaxDepthExceeded);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a/b")]
    public void Should_ReturnInvalidName_When_NameEmptyAfterTrimOrHasSlash(string name)
    {
        // Arrange & Act
        Result<Folder> result = Folder.CreateRoot(name, Rules);

        // Assert (AC-6)
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(FolderErrors.InvalidName);
    }

    [Fact]
    public void Should_ReturnInvalidName_When_NameTooLong()
    {
        // Arrange
        var name = new string('x', 101);

        // Act
        Result<Folder> result = Folder.CreateRoot(name, Rules);

        // Assert (AC-6)
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(FolderErrors.InvalidName);
    }

    [Fact]
    public void Should_TrimName_When_Creating()
    {
        // Arrange & Act
        Result<Folder> result = Folder.CreateRoot("  Umowy  ", Rules);

        // Assert
        result.Value.Name.Should().Be("Umowy");
    }

    [Fact]
    public void Should_ChangeNameOnly_When_Renaming()
    {
        // Arrange
        Folder folder = Folder.CreateRoot("Umowy", Rules).Value;
        string originalPath = folder.Path;
        Guid? originalParent = folder.ParentId;

        // Act (AC-4)
        Result result = folder.Rename("Umowy 2026", Rules);

        // Assert — name changes; path and parent are untouched (segments are ids, not names).
        result.IsSuccess.Should().BeTrue();
        folder.Name.Should().Be("Umowy 2026");
        folder.Path.Should().Be(originalPath);
        folder.ParentId.Should().Be(originalParent);
    }

    [Fact]
    public void Should_Succeed_When_RenamingToCurrentName()
    {
        // Arrange
        Folder folder = Folder.CreateRoot("Umowy", Rules).Value;

        // Act — a no-op rename (post-trim same value) is a success, not a duplicate failure.
        Result result = folder.Rename("  Umowy  ", Rules);

        // Assert
        result.IsSuccess.Should().BeTrue();
        folder.Name.Should().Be("Umowy");
    }

    [Fact]
    public void Should_ReturnInvalidName_When_RenamingToInvalidName()
    {
        // Arrange
        Folder folder = Folder.CreateRoot("Umowy", Rules).Value;

        // Act
        Result result = folder.Rename("bad/name", Rules);

        // Assert (AC-6)
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(FolderErrors.InvalidName);
    }
}
