using FluentAssertions;
using NSubstitute;
using RagBook.Modules.Documents.Domain;
using RagBook.Modules.Documents.Errors;
using RagBook.Modules.Documents.Features.DeleteDocument;
using RagBook.Shared.Results;
using Xunit;

namespace RagBook.Application.Tests.Documents;

public sealed class DeleteDocumentCommandHandlerTests
{
    private readonly IDocumentDeletionRepository _repository = Substitute.For<IDocumentDeletionRepository>();

    private DeleteDocumentCommandHandler CreateSut()
    {
        return new DeleteDocumentCommandHandler(_repository);
    }

    [Fact]
    public async Task Should_Delete_When_Present()
    {
        // Arrange (AC-1)
        var id = Guid.NewGuid();
        _repository.DeleteAsync(id, Arg.Any<CancellationToken>()).Returns(true);
        var sut = CreateSut();

        // Act
        Result result = await sut.Handle(new DeleteDocumentCommand(id), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        await _repository.Received(1).DeleteAsync(id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_ReturnNotFound_When_DocumentMissing()
    {
        // Arrange (AC-4/AC-5 — cross-session / already-deleted / unknown reads as not found)
        _repository.DeleteAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(false);
        var sut = CreateSut();

        // Act
        Result result = await sut.Handle(new DeleteDocumentCommand(Guid.NewGuid()), CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(DocumentErrors.NotFound);
    }
}
