using FluentAssertions;
using RagBook.Modules.Documents.Domain;
using RagBook.Modules.Documents.Errors;
using RagBook.Shared.Results;
using Xunit;

namespace RagBook.Domain.Tests.Documents;

public sealed class DocumentUploadTests
{
    private static readonly DateTimeOffset UploadedAt = new(2026, 7, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Should_CreateProcessingUploadDocument_When_Valid()
    {
        // Arrange
        var folderId = Guid.NewGuid();

        // Act
        Result<Document> result = Document.CreateUpload(
            sizeBytes: 1_234,
            fileName: "umowa.pdf",
            contentType: "application/pdf",
            folderId: folderId,
            storagePath: "sess/blob.pdf",
            uploadedAt: UploadedAt);

        // Assert
        result.IsSuccess.Should().BeTrue();
        Document document = result.Value;
        document.Status.Should().Be(DocumentStatus.Processing);
        document.Origin.Should().Be(DocumentOrigin.User);
        document.FolderId.Should().Be(folderId);
        document.FileName.Should().Be("umowa.pdf");
        document.ContentType.Should().Be("application/pdf");
        document.StoragePath.Should().Be("sess/blob.pdf");
        document.UploadedAt.Should().Be(UploadedAt);
        document.ChunkCount.Should().Be(0);
        document.UserSessionId.Should().Be(Guid.Empty);
    }

    [Fact]
    public void Should_CreateRootDocument_When_NoFolder()
    {
        // Arrange & Act
        Result<Document> result = Document.CreateUpload(10, "a.txt", "text/plain", null, "p", UploadedAt);

        // Assert
        result.Value.FolderId.Should().BeNull();
    }

    [Fact]
    public void Should_ReturnEmptyFile_When_SizeZero()
    {
        // Arrange & Act (FR-004)
        Result<Document> result = Document.CreateUpload(0, "a.txt", "text/plain", null, "p", UploadedAt);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(DocumentErrors.EmptyFile);
    }

    [Fact]
    public void Should_RenameForSuffix_When_Deduplicating()
    {
        // Arrange
        Document document = Document.CreateUpload(10, "umowa.pdf", "application/pdf", null, "p", UploadedAt).Value;

        // Act
        document.RenameForSuffix("umowa (1).pdf");

        // Assert
        document.FileName.Should().Be("umowa (1).pdf");
    }
}
