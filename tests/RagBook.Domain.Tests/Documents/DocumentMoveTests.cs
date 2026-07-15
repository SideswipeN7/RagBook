using FluentAssertions;
using RagBook.Modules.Documents.Domain;
using Xunit;

namespace RagBook.Domain.Tests.Documents;

/// <summary>Unit tests for <see cref="Document.MoveToFolder"/> (US-10): it changes only the owning folder.</summary>
public sealed class DocumentMoveTests
{
    private static Document Upload(Guid? folderId)
    {
        return Document.CreateUpload(10, "a.pdf", "application/pdf", folderId, "storage/a", DateTimeOffset.UtcNow).Value;
    }

    [Fact]
    public void Should_MoveToFolder()
    {
        // Arrange
        var folderId = Guid.NewGuid();
        Document document = Upload(folderId: null);

        // Act
        document.MoveToFolder(folderId);

        // Assert
        document.FolderId.Should().Be(folderId);
    }

    [Fact]
    public void Should_MoveToRoot_When_FolderIdNull()
    {
        // Arrange
        Document document = Upload(Guid.NewGuid());

        // Act
        document.MoveToFolder(null);

        // Assert
        document.FolderId.Should().BeNull();
    }
}
