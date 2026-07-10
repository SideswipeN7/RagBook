using FluentAssertions;
using NSubstitute;
using RagBook.Modules.Folders.Domain;
using RagBook.Modules.Folders.Errors;
using RagBook.Modules.Folders.Features.DeleteFolder;
using RagBook.Shared.Results;
using Xunit;

namespace RagBook.Application.Tests.Folders;

public sealed class DeleteFolderCommandHandlerTests
{
    private readonly IFolderRepository _repository = Substitute.For<IFolderRepository>();
    private readonly IFolderFileProbe _fileProbe = Substitute.For<IFolderFileProbe>();

    private DeleteFolderCommandHandler CreateSut()
    {
        return new DeleteFolderCommandHandler(_repository, _fileProbe);
    }

    private Folder GivenExistingFolder()
    {
        Folder folder = Folder.CreateRoot("Umowy", new FolderNameRules(100)).Value;
        _repository.GetByIdAsync(folder.Id, Arg.Any<CancellationToken>()).Returns(folder);

        return folder;
    }

    [Fact]
    public async Task Should_Delete_When_Empty()
    {
        // Arrange
        Folder folder = GivenExistingFolder();
        _repository.HasChildrenAsync(folder.Id, Arg.Any<CancellationToken>()).Returns(false);
        _fileProbe.HasFilesAsync(folder.Id, Arg.Any<CancellationToken>()).Returns(false);
        var sut = CreateSut();

        // Act (AC-5)
        Result result = await sut.Handle(new DeleteFolderCommand(folder.Id), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _repository.Received(1).Remove(folder);
        await _repository.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_ReturnNotEmpty_When_HasChild()
    {
        // Arrange
        Folder folder = GivenExistingFolder();
        _repository.HasChildrenAsync(folder.Id, Arg.Any<CancellationToken>()).Returns(true);
        var sut = CreateSut();

        // Act (AC-5)
        Result result = await sut.Handle(new DeleteFolderCommand(folder.Id), CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(FolderErrors.NotEmpty);
        _repository.DidNotReceive().Remove(Arg.Any<Folder>());
    }

    [Fact]
    public async Task Should_ReturnNotEmpty_When_HasFiles()
    {
        // Arrange
        Folder folder = GivenExistingFolder();
        _repository.HasChildrenAsync(folder.Id, Arg.Any<CancellationToken>()).Returns(false);
        _fileProbe.HasFilesAsync(folder.Id, Arg.Any<CancellationToken>()).Returns(true);
        var sut = CreateSut();

        // Act (AC-5 file arm — proven via a fake probe until US-04 wires the real query)
        Result result = await sut.Handle(new DeleteFolderCommand(folder.Id), CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(FolderErrors.NotEmpty);
        _repository.DidNotReceive().Remove(Arg.Any<Folder>());
    }

    [Fact]
    public async Task Should_ReturnNotFound_When_Missing()
    {
        // Arrange
        _repository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Folder?)null);
        var sut = CreateSut();

        // Act
        Result result = await sut.Handle(new DeleteFolderCommand(Guid.NewGuid()), CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(FolderErrors.NotFound);
    }
}
