using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using RagBook.Modules.Folders;
using RagBook.Modules.Folders.Domain;
using RagBook.Modules.Folders.Errors;
using RagBook.Modules.Folders.Features.RenameFolder;
using RagBook.Shared.Results;
using Xunit;

namespace RagBook.Application.Tests.Folders;

public sealed class RenameFolderCommandHandlerTests
{
    private readonly IFolderRepository _repository = Substitute.For<IFolderRepository>();
    private readonly IOptions<FolderOptions> _options = Options.Create(new FolderOptions());

    private RenameFolderCommandHandler CreateSut()
    {
        return new RenameFolderCommandHandler(_repository, _options);
    }

    [Fact]
    public async Task Should_Rename_When_NameValid()
    {
        // Arrange
        Folder folder = Folder.CreateRoot("Umowy", new FolderNameRules(100)).Value;
        _repository.GetByIdAsync(folder.Id, Arg.Any<CancellationToken>()).Returns(folder);
        var sut = CreateSut();

        // Act (AC-4)
        Result result = await sut.Handle(new RenameFolderCommand(folder.Id, "Umowy 2026"), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        folder.Name.Should().Be("Umowy 2026");
        await _repository.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_ReturnNotFound_When_FolderMissing()
    {
        // Arrange
        _repository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Folder?)null);
        var sut = CreateSut();

        // Act
        Result result = await sut.Handle(new RenameFolderCommand(Guid.NewGuid(), "X"), CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(FolderErrors.NotFound);
        await _repository.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_ReturnInvalidName_When_NewNameInvalid()
    {
        // Arrange
        Folder folder = Folder.CreateRoot("Umowy", new FolderNameRules(100)).Value;
        _repository.GetByIdAsync(folder.Id, Arg.Any<CancellationToken>()).Returns(folder);
        var sut = CreateSut();

        // Act (AC-6)
        Result result = await sut.Handle(new RenameFolderCommand(folder.Id, "   "), CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(FolderErrors.InvalidName);
        await _repository.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
