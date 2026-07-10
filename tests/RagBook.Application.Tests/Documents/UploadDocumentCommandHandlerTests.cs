using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using RagBook.Modules.Documents.Domain;
using RagBook.Modules.Documents.Errors;
using RagBook.Modules.Documents.Features.UploadDocument;
using RagBook.Modules.Documents.Quota;
using RagBook.Shared.Messaging;
using RagBook.Shared.Results;
using Xunit;

namespace RagBook.Application.Tests.Documents;

public sealed class UploadDocumentCommandHandlerTests
{
    private readonly IFileStorage _fileStorage = Substitute.For<IFileStorage>();
    private readonly IDocumentUploadRepository _uploadRepository = Substitute.For<IDocumentUploadRepository>();
    private readonly IFolderReference _folderReference = Substitute.For<IFolderReference>();
    private readonly IEventPublisher _eventPublisher = Substitute.For<IEventPublisher>();
    private QuotaOptions _quota = new() { MaxDocuments = 10, MaxFileSizeMb = 10, MaxTotalMb = 50 };

    private static readonly byte[] ValidPdf = Encoding.ASCII.GetBytes("%PDF-1.7\nhello");

    private UploadDocumentCommandHandler CreateSut()
    {
        _fileStorage.SaveAsync(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("session/blob.pdf");

        return new UploadDocumentCommandHandler(
            _fileStorage,
            _uploadRepository,
            _folderReference,
            Options.Create(_quota),
            TimeProvider.System,
            _eventPublisher);
    }

    [Fact]
    public async Task Should_StoreAdmitAndPublish_When_ValidUpload()
    {
        // Arrange
        _uploadRepository.AddUploadedWithinQuotaAsync(Arg.Any<Document>(), Arg.Any<QuotaLimits>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        var sut = CreateSut();

        // Act (AC-1)
        Result<DocumentResponse> result = await sut.Handle(
            new UploadDocumentCommand("umowa.pdf", "application/pdf", ValidPdf, FolderId: null),
            CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.ContentType.Should().Be("application/pdf");
        result.Value.Status.Should().Be("Processing");
        await _fileStorage.Received(1).SaveAsync(Arg.Any<Stream>(), "umowa.pdf", Arg.Any<CancellationToken>());
        await _eventPublisher.Received(1).PublishAsync(Arg.Any<DocumentUploaded>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_ReturnNotFound_When_TargetFolderInAnotherSession()
    {
        // Arrange (FR-006)
        _folderReference.ExistsInSessionAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(false);
        var sut = CreateSut();

        // Act
        Result<DocumentResponse> result = await sut.Handle(
            new UploadDocumentCommand("umowa.pdf", "application/pdf", ValidPdf, Guid.NewGuid()),
            CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(DocumentErrors.TargetFolderNotFound);
        await _fileStorage.DidNotReceive().SaveAsync(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_RejectEmpty_When_ZeroBytes()
    {
        // Arrange (FR-004)
        var sut = CreateSut();

        // Act
        Result<DocumentResponse> result = await sut.Handle(
            new UploadDocumentCommand("a.txt", "text/plain", [], FolderId: null),
            CancellationToken.None);

        // Assert
        result.Error.Should().Be(DocumentErrors.EmptyFile);
        await _fileStorage.DidNotReceive().SaveAsync(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_RejectUnsupported_When_NotPdfNorText()
    {
        // Arrange (AC-2)
        var sut = CreateSut();

        // Act
        Result<DocumentResponse> result = await sut.Handle(
            new UploadDocumentCommand("fake.pdf", "application/pdf", [0x4D, 0x5A, 0x00, 0x01], FolderId: null),
            CancellationToken.None);

        // Assert
        result.Error.Should().Be(DocumentErrors.UnsupportedFileType);
        await _fileStorage.DidNotReceive().SaveAsync(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_RejectOversize_When_ExceedsPerFileLimit()
    {
        // Arrange (AC-3) — 1 MB limit; a 1,000,001-byte text file is over it.
        _quota = new QuotaOptions { MaxDocuments = 10, MaxFileSizeMb = 1, MaxTotalMb = 50 };
        byte[] big = Enumerable.Repeat((byte)'a', 1_000_001).ToArray();
        var sut = CreateSut();

        // Act
        Result<DocumentResponse> result = await sut.Handle(
            new UploadDocumentCommand("big.txt", "text/plain", big, FolderId: null),
            CancellationToken.None);

        // Assert
        result.Error.Code.Should().Be("quota.file_too_large");
        await _fileStorage.DidNotReceive().SaveAsync(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_DeleteBlob_When_AdmitFails()
    {
        // Arrange (FR-012) — storage succeeds, quota admit rejects → the stored blob is cleaned up.
        _uploadRepository.AddUploadedWithinQuotaAsync(Arg.Any<Document>(), Arg.Any<QuotaLimits>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure(QuotaErrors.QuotaExceeded));
        var sut = CreateSut();

        // Act
        Result<DocumentResponse> result = await sut.Handle(
            new UploadDocumentCommand("umowa.pdf", "application/pdf", ValidPdf, FolderId: null),
            CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(QuotaErrors.QuotaExceeded);
        await _fileStorage.Received(1).DeleteAsync("session/blob.pdf", Arg.Any<CancellationToken>());
        await _eventPublisher.DidNotReceive().PublishAsync(Arg.Any<DocumentUploaded>(), Arg.Any<CancellationToken>());
    }
}
