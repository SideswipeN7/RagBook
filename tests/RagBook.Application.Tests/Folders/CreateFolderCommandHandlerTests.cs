using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using RagBook.Modules.Folders;
using RagBook.Modules.Folders.Domain;
using RagBook.Modules.Folders.Errors;
using RagBook.Modules.Folders.Features.CreateFolder;
using RagBook.Shared.Results;
using Xunit;

namespace RagBook.Application.Tests.Folders;

public sealed class CreateFolderCommandHandlerTests
{
    private readonly IFolderRepository _repository = Substitute.For<IFolderRepository>();
    private readonly IOptions<FolderOptions> _options = Options.Create(new FolderOptions());

    private CreateFolderCommandHandler CreateSut()
    {
        return new CreateFolderCommandHandler(_repository, _options);
    }

    [Fact]
    public async Task Should_CreateRoot_When_ParentIdNull()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        Result<Guid> result = await sut.Handle(new CreateFolderCommand("Umowy", ParentId: null), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        await _repository.Received(1).AddAsync(
            Arg.Is<Folder>(folder => folder.ParentId == null && folder.Name == "Umowy"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_CreateChildUnderParent_When_ParentIdGiven()
    {
        // Arrange
        Folder parent = Folder.CreateRoot("Umowy", new FolderNameRules(100)).Value;
        _repository.GetByIdAsync(parent.Id, Arg.Any<CancellationToken>()).Returns(parent);
        var sut = CreateSut();

        // Act
        Result<Guid> result = await sut.Handle(new CreateFolderCommand("2026", parent.Id), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        await _repository.Received(1).AddAsync(
            Arg.Is<Folder>(folder => folder.ParentId == parent.Id),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_ReturnNotFound_When_ParentInAnotherSession()
    {
        // Arrange — the parent id is invisible to this session, so the repository returns null (FR-010).
        _repository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Folder?)null);
        var sut = CreateSut();

        // Act
        Result<Guid> result = await sut.Handle(new CreateFolderCommand("2026", Guid.NewGuid()), CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(FolderErrors.NotFound);
        await _repository.DidNotReceive().AddAsync(Arg.Any<Folder>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_ReturnInvalidName_When_NameInvalid()
    {
        // Arrange
        var sut = CreateSut();

        // Act (AC-6)
        Result<Guid> result = await sut.Handle(new CreateFolderCommand("bad/name", ParentId: null), CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(FolderErrors.InvalidName);
        await _repository.DidNotReceive().AddAsync(Arg.Any<Folder>(), Arg.Any<CancellationToken>());
    }
}
