using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using RagBook.Modules.Documents;
using RagBook.Modules.Documents.Domain;
using RagBook.Modules.Documents.Features.BulkMove;
using Xunit;

namespace RagBook.Application.Tests.Documents;

/// <summary>
/// Unit tests for <see cref="BulkMoveCommandHandler"/> (US-12): all-or-nothing validation (empty / over-cap → 400;
/// not-found / read-only / missing target folder → per-id failures with no write), de-duplication, and the
/// successful move of every selected document in one call.
/// </summary>
public sealed class BulkMoveHandlerTests
{
    private readonly IDocumentBulkRepository _repository = Substitute.For<IDocumentBulkRepository>();
    private readonly IFolderReference _folders = Substitute.For<IFolderReference>();

    private BulkMoveCommandHandler CreateSut(int maxItems = 50)
    {
        _folders.ExistsInSessionAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);
        return new BulkMoveCommandHandler(_repository, _folders, Options.Create(new BulkOptions { MaxItems = maxItems }));
    }

    private static Document Upload()
    {
        return Document.CreateUpload(10, "a.pdf", "application/pdf", null, "storage/a", DateTimeOffset.UtcNow).Value;
    }

    [Fact]
    public async Task Should_MoveAll_When_EveryDocumentValid()
    {
        // Arrange
        Document a = Upload();
        Document b = Upload();
        _repository.GetByIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([a, b]);
        var target = Guid.NewGuid();

        // Act
        BulkResult result = await CreateSut().Handle(new BulkMoveCommand([a.Id, b.Id], target), CancellationToken.None);

        // Assert
        result.Outcome.Should().Be(BulkOutcome.Success);
        await _repository.Received(1).MoveAllAsync(
            Arg.Is<IReadOnlyList<Document>>(docs => docs.Count == 2), target, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_RejectAll_When_OneIdNotInSession()
    {
        // Arrange — only `a` comes back; `missing` is absent (cross-session / unknown).
        Document a = Upload();
        var missing = Guid.NewGuid();
        _repository.GetByIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([a]);

        // Act
        BulkResult result = await CreateSut().Handle(new BulkMoveCommand([a.Id, missing], null), CancellationToken.None);

        // Assert — 422 failures, and NO move at all.
        result.Outcome.Should().Be(BulkOutcome.ValidationFailed);
        result.Failures.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new BulkFailure(missing, "document.not_found"));
        await _repository.DidNotReceive().MoveAllAsync(
            Arg.Any<IReadOnlyList<Document>>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_RejectAll_When_OneIsReadOnlyDemo()
    {
        // Arrange
        Document a = Upload();
        Document demo = Document.CreateForQuota(10, DocumentOrigin.Demo).Value;
        _repository.GetByIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([a, demo]);

        // Act
        BulkResult result = await CreateSut().Handle(new BulkMoveCommand([a.Id, demo.Id], null), CancellationToken.None);

        // Assert
        result.Outcome.Should().Be(BulkOutcome.ValidationFailed);
        result.Failures.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new BulkFailure(demo.Id, "document.read_only"));
        await _repository.DidNotReceive().MoveAllAsync(
            Arg.Any<IReadOnlyList<Document>>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_RejectAll_When_TargetFolderMissing()
    {
        // Arrange
        Document a = Upload();
        _repository.GetByIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([a]);
        var target = Guid.NewGuid();
        BulkMoveCommandHandler sut = CreateSut();
        _folders.ExistsInSessionAsync(target, Arg.Any<CancellationToken>()).Returns(false);

        // Act
        BulkResult result = await sut.Handle(new BulkMoveCommand([a.Id], target), CancellationToken.None);

        // Assert — the target folder id is the failing item.
        result.Outcome.Should().Be(BulkOutcome.ValidationFailed);
        result.Failures.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new BulkFailure(target, "folder.not_found"));
        await _repository.DidNotReceive().MoveAllAsync(
            Arg.Any<IReadOnlyList<Document>>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_Deduplicate_BeforeMoving()
    {
        // Arrange — the same id appears twice in the request.
        Document a = Upload();
        _repository.GetByIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([a]);

        // Act
        BulkResult result = await CreateSut().Handle(new BulkMoveCommand([a.Id, a.Id], null), CancellationToken.None);

        // Assert — de-duplicated to one, no spurious not-found.
        result.Outcome.Should().Be(BulkOutcome.Success);
        await _repository.Received(1).GetByIdsAsync(
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 1), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_ReturnBadRequest_When_ListEmpty()
    {
        // Act
        BulkResult result = await CreateSut().Handle(new BulkMoveCommand([], null), CancellationToken.None);

        // Assert
        result.Outcome.Should().Be(BulkOutcome.BadRequest);
        result.Error!.Code.Should().Be("document.bulk_empty");
    }

    [Fact]
    public async Task Should_ReturnBadRequest_When_ListOverCap()
    {
        // Arrange — three distinct ids against a cap of two.
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };

        // Act
        BulkResult result = await CreateSut(maxItems: 2).Handle(new BulkMoveCommand(ids, null), CancellationToken.None);

        // Assert
        result.Outcome.Should().Be(BulkOutcome.BadRequest);
        result.Error!.Code.Should().Be("document.bulk_too_large");
    }
}
