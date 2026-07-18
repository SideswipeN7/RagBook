using FluentAssertions;
using NSubstitute;
using RagBook.Modules.Tree.Domain;
using RagBook.Modules.Tree.Features.GetTree;
using Xunit;

namespace RagBook.Application.Tests.Tree;

public sealed class GetTreeQueryHandlerTests
{
    private readonly ITreeReader _treeReader = Substitute.For<ITreeReader>();

    private GetTreeQueryHandler CreateSut()
    {
        return new GetTreeQueryHandler(_treeReader);
    }

    [Fact]
    public async Task Should_ReturnReadersFoldersAndDocuments_PreservingOrder()
    {
        // Arrange — the reader is responsible for ordering (folders A→Z, documents newest-first);
        // the handler must pass both lists through unchanged (FR-001/FR-008).
        var rootId = Guid.NewGuid();
        var folders = new List<TreeFolder>
        {
            new(rootId, null, "Ananas", 1),
            new(Guid.NewGuid(), null, "banan", 1),
        };
        var newer = new TreeDocument(Guid.NewGuid(), rootId, "b.pdf", "application/pdf", 20, "Ready", 3,
            new DateTimeOffset(2026, 7, 11, 12, 0, 0, TimeSpan.Zero), null);
        var older = new TreeDocument(Guid.NewGuid(), null, "a.pdf", "application/pdf", 10, "Processing", 0,
            new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero), null);
        var documents = new List<TreeDocument> { newer, older };
        var demo = new TreeDocument(Guid.NewGuid(), null, "demo.pdf", "application/pdf", 30, "Ready", 5,
            new DateTimeOffset(2026, 7, 9, 12, 0, 0, TimeSpan.Zero), null);
        _treeReader.GetAsync(Arg.Any<CancellationToken>()).Returns(new TreeData(folders, documents, [demo]));
        var sut = CreateSut();

        // Act
        TreeResponse response = await sut.Handle(new GetTreeQuery(), CancellationToken.None);

        // Assert — exact contents and order preserved; demo documents passed through separately (US-03).
        response.Folders.Select(f => f.Name).Should().ContainInOrder("Ananas", "banan");
        response.Documents.Should().ContainInOrder(newer, older);
        response.Documents[0].Status.Should().Be("Ready");
        response.Demo.Should().ContainSingle().Which.Should().Be(demo);
    }

    [Fact]
    public async Task Should_ReturnEmptyLists_When_SessionHasNothing()
    {
        // Arrange
        _treeReader.GetAsync(Arg.Any<CancellationToken>())
            .Returns(new TreeData([], [], []));
        var sut = CreateSut();

        // Act
        TreeResponse response = await sut.Handle(new GetTreeQuery(), CancellationToken.None);

        // Assert
        response.Folders.Should().BeEmpty();
        response.Documents.Should().BeEmpty();
        response.Demo.Should().BeEmpty();
    }
}
