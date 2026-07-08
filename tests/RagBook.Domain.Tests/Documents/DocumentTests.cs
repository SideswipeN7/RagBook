using FluentAssertions;
using RagBook.Modules.Documents.Domain;
using RagBook.Modules.Documents.Errors;
using RagBook.Shared.Results;
using Xunit;

namespace RagBook.Domain.Tests.Documents;

public sealed class DocumentTests
{
    [Fact]
    public void Should_CreateProcessingUserDocument_When_CreatedForQuota()
    {
        // Arrange
        const long sizeBytes = 1_234;

        // Act
        Result<Document> result = Document.CreateForQuota(sizeBytes, DocumentOrigin.User);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(DocumentStatus.Processing);
        result.Value.Origin.Should().Be(DocumentOrigin.User);
        result.Value.SizeBytes.Should().Be(sizeBytes);
        result.Value.Id.Version.Should().Be(4);
    }

    [Fact]
    public void Should_NotStampSession_When_Created()
    {
        // Arrange & Act
        Result<Document> result = Document.CreateForQuota(10, DocumentOrigin.User);

        // Assert — the owning session is stamped centrally on save, never by the aggregate.
        result.Value.UserSessionId.Should().Be(Guid.Empty);
    }

    [Fact]
    public void Should_ReturnInvalidSize_When_SizeIsNegative()
    {
        // Arrange & Act
        Result<Document> result = Document.CreateForQuota(-1, DocumentOrigin.User);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(QuotaErrors.InvalidSize);
    }
}
