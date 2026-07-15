using FluentAssertions;
using NSubstitute;
using RagBook.Modules.Documents.Domain;
using RagBook.Modules.Documents.Features.MoveDocument;
using RagBook.Shared.Results;
using Xunit;

namespace RagBook.Application.Tests.Documents;

/// <summary>
/// Unit tests for <see cref="MoveDocumentCommandHandler"/> (US-10): ownership + read-only + target-folder
/// validation, the same-folder no-op, and the successful move.
/// </summary>
public sealed class MoveDocumentHandlerTests
{
    private readonly IDocumentMoveRepository _repository = Substitute.For<IDocumentMoveRepository>();
    private readonly IFolderReference _folders = Substitute.For<IFolderReference>();

    private MoveDocumentCommandHandler CreateSut()
    {
        return new MoveDocumentCommandHandler(_repository, _folders);
    }

    private static Document Upload(Guid? folderId)
    {
        return Document.CreateUpload(10, "a.pdf", "application/pdf", folderId, "storage/a", DateTimeOffset.UtcNow).Value;
    }

    [Fact]
    public async Task Should_ReturnNotFound_When_DocumentAbsent()
    {
        // Arrange — a cross-session/unknown document reads as null.
        _repository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Document?)null);

        // Act
        Result result = await CreateSut().Handle(new MoveDocumentCommand(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        // Assert
        result.Error.Code.Should().Be("document.not_found");
    }

    [Fact]
    public async Task Should_ReturnReadOnly_When_DemoDocument()
    {
        // Arrange
        Document demo = Document.CreateForQuota(10, DocumentOrigin.Demo).Value;
        _repository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(demo);

        // Act
        Result result = await CreateSut().Handle(new MoveDocumentCommand(demo.Id, Guid.NewGuid()), CancellationToken.None);

        // Assert
        result.Error.Code.Should().Be("document.read_only");
        await _repository.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_ReturnFolderNotFound_When_TargetFolderMissing()
    {
        // Arrange
        Document document = Upload(folderId: null);
        _repository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(document);
        _folders.ExistsInSessionAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(false);

        // Act
        Result result = await CreateSut().Handle(new MoveDocumentCommand(document.Id, Guid.NewGuid()), CancellationToken.None);

        // Assert
        result.Error.Code.Should().Be("folder.not_found");
    }

    [Fact]
    public async Task Should_NoOp_When_AlreadyInTargetFolder()
    {
        // Arrange — the document is already in the target folder.
        var folderId = Guid.NewGuid();
        Document document = Upload(folderId);
        _repository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(document);
        _folders.ExistsInSessionAsync(folderId, Arg.Any<CancellationToken>()).Returns(true);

        // Act
        Result result = await CreateSut().Handle(new MoveDocumentCommand(document.Id, folderId), CancellationToken.None);

        // Assert — success, but no write.
        result.IsSuccess.Should().BeTrue();
        await _repository.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_MoveAndSave_When_ValidTarget()
    {
        // Arrange
        var folderId = Guid.NewGuid();
        Document document = Upload(folderId: null);
        _repository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(document);
        _folders.ExistsInSessionAsync(folderId, Arg.Any<CancellationToken>()).Returns(true);

        // Act
        Result result = await CreateSut().Handle(new MoveDocumentCommand(document.Id, folderId), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        document.FolderId.Should().Be(folderId);
        await _repository.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
