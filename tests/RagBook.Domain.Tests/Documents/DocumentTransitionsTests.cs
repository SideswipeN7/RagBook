using FluentAssertions;
using RagBook.Modules.Documents.Domain;
using Xunit;

namespace RagBook.Domain.Tests.Documents;

public sealed class DocumentTransitionsTests
{
    private static readonly DateTimeOffset UploadedAt = new(2026, 7, 11, 12, 0, 0, TimeSpan.Zero);

    private static Document Uploaded()
    {
        return Document.CreateUpload(1_000, "umowa.pdf", "application/pdf", folderId: null, "blob", UploadedAt).Value;
    }

    [Fact]
    public void Should_MarkReadyWithChunkCount()
    {
        // Arrange
        Document document = Uploaded();

        // Act (US-06 AC-1)
        document.MarkReady(chunkCount: 8);

        // Assert
        document.Status.Should().Be(DocumentStatus.Ready);
        document.ChunkCount.Should().Be(8);
        document.FailureReason.Should().BeNull();
    }

    [Fact]
    public void Should_MarkFailedWithReasonAndZeroChunks()
    {
        // Arrange
        Document document = Uploaded();
        document.MarkReady(5); // even if previously ready...

        // Act (US-06 AC-2/AC-3)
        document.MarkFailed("PDF nie zawiera tekstu.");

        // Assert
        document.Status.Should().Be(DocumentStatus.Failed);
        document.FailureReason.Should().Be("PDF nie zawiera tekstu.");
        document.ChunkCount.Should().Be(0);
    }
}
