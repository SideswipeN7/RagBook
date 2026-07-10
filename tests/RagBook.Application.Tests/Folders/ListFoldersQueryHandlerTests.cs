using FluentAssertions;
using NSubstitute;
using RagBook.Modules.Folders.Domain;
using RagBook.Modules.Folders.Features.ListFolders;
using Xunit;

namespace RagBook.Application.Tests.Folders;

public sealed class ListFoldersQueryHandlerTests
{
    private readonly IFolderRepository _repository = Substitute.For<IFolderRepository>();

    private ListFoldersQueryHandler CreateSut()
    {
        return new ListFoldersQueryHandler(_repository);
    }

    [Fact]
    public async Task Should_MapFoldersToNodesWithDepth_When_Listing()
    {
        // Arrange — the repository is responsible for ordering (FR-013); the handler maps + derives depth.
        var rules = new FolderNameRules(100);
        Folder root = Folder.CreateRoot("Umowy", rules).Value;
        Folder child = Folder.CreateChild(root, "2026", rules, maxDepth: 3).Value;
        _repository.ListForSessionAsync(Arg.Any<CancellationToken>())
            .Returns(new List<Folder> { root, child });
        var sut = CreateSut();

        // Act
        IReadOnlyList<FolderNode> nodes = await sut.Handle(new ListFoldersQuery(), CancellationToken.None);

        // Assert
        nodes.Should().HaveCount(2);
        nodes[0].Should().BeEquivalentTo(new FolderNode(root.Id, null, "Umowy", 1));
        nodes[1].Should().BeEquivalentTo(new FolderNode(child.Id, root.Id, "2026", 2));
    }
}
