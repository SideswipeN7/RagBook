using FluentAssertions;
using RagBook.Modules.Folders.Domain;
using Xunit;

namespace RagBook.Domain.Tests.Folders;

public sealed class FolderPathTests
{
    [Fact]
    public void Should_HaveDepthOne_When_Root()
    {
        // Arrange & Act
        FolderPath path = FolderPath.ForRoot(Guid.NewGuid());

        // Assert
        path.Depth.Should().Be(1);
    }

    [Fact]
    public void Should_WrapSegmentsWithLeadingAndTrailingSlash()
    {
        // Arrange
        var id = Guid.NewGuid();

        // Act
        FolderPath path = FolderPath.ForRoot(id);

        // Assert
        path.Value.Should().Be($"/{id:N}/");
    }

    [Fact]
    public void Should_AppendSegmentAndIncreaseDepth_When_AddingChild()
    {
        // Arrange
        var rootId = Guid.NewGuid();
        var childId = Guid.NewGuid();

        // Act
        FolderPath child = FolderPath.ForRoot(rootId).Append(childId);

        // Assert
        child.Depth.Should().Be(2);
        child.Value.Should().Be($"/{rootId:N}/{childId:N}/");
        child.Segments.Should().Equal(rootId, childId);
    }

    [Fact]
    public void Should_RoundTrip_When_Parsed()
    {
        // Arrange
        FolderPath original = FolderPath.ForRoot(Guid.NewGuid()).Append(Guid.NewGuid());

        // Act
        FolderPath parsed = FolderPath.Parse(original.Value);

        // Assert
        parsed.Value.Should().Be(original.Value);
        parsed.Depth.Should().Be(2);
    }

    [Fact]
    public void Should_DetectPrefix_When_AncestorOfDescendant()
    {
        // Arrange
        var rootId = Guid.NewGuid();
        FolderPath ancestor = FolderPath.ForRoot(rootId);
        FolderPath descendant = ancestor.Append(Guid.NewGuid());

        // Act & Assert
        ancestor.IsPrefixOf(descendant).Should().BeTrue();
        descendant.IsPrefixOf(ancestor).Should().BeFalse();
        descendant.Contains(rootId).Should().BeTrue();
    }
}
