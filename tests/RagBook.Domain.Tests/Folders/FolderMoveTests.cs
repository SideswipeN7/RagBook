using FluentAssertions;
using RagBook.Modules.Folders.Domain;
using Xunit;

namespace RagBook.Domain.Tests.Folders;

/// <summary>
/// Unit tests for the cycle primitive of a folder move (US-11): <see cref="FolderPath.IsPrefixOf"/> is true for a
/// folder against itself and against any descendant (both are refused as circular), and false otherwise.
/// </summary>
public sealed class FolderMoveTests
{
    [Fact]
    public void Should_BePrefixOfItself_SoSelfMoveIsCircular()
    {
        // Arrange
        FolderPath a = FolderPath.ForRoot(Guid.NewGuid());

        // Act + Assert — moving A into A: target path == moved path.
        a.IsPrefixOf(a).Should().BeTrue();
    }

    [Fact]
    public void Should_BePrefixOfDescendant_SoDescendantMoveIsCircular()
    {
        // Arrange — A, and A/B beneath it.
        var aId = Guid.NewGuid();
        FolderPath a = FolderPath.ForRoot(aId);
        FolderPath b = a.Append(Guid.NewGuid());

        // Act + Assert — A is an ancestor-or-self prefix of A/B.
        a.IsPrefixOf(b).Should().BeTrue();
    }

    [Fact]
    public void Should_NotBePrefixOf_UnrelatedFolder()
    {
        // Arrange — two independent root folders.
        FolderPath a = FolderPath.ForRoot(Guid.NewGuid());
        FolderPath other = FolderPath.ForRoot(Guid.NewGuid());

        // Act + Assert — neither contains the other.
        a.IsPrefixOf(other).Should().BeFalse();
        other.IsPrefixOf(a).Should().BeFalse();
    }

    [Fact]
    public void Should_NotBePrefixOf_ItsOwnAncestor()
    {
        // Arrange — A/B; B is not an ancestor of A (moving A under B would be caught, but B is deeper).
        FolderPath a = FolderPath.ForRoot(Guid.NewGuid());
        FolderPath b = a.Append(Guid.NewGuid());

        // Act + Assert — the descendant is not a prefix of its ancestor.
        b.IsPrefixOf(a).Should().BeFalse();
    }
}
