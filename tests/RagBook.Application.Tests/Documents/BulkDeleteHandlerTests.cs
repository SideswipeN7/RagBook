using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using RagBook.Modules.Documents;
using RagBook.Modules.Documents.Domain;
using RagBook.Modules.Documents.Features.BulkDelete;
using Xunit;

namespace RagBook.Application.Tests.Documents;

/// <summary>
/// Unit tests for <see cref="BulkDeleteCommandHandler"/> (US-12): all-or-nothing validation (empty / over-cap →
/// 400; not-found / read-only → per-id failures with no delete), de-duplication, and the successful delete of
/// every selected document in one call.
/// </summary>
public sealed class BulkDeleteHandlerTests
{
    private readonly IDocumentBulkRepository _repository = Substitute.For<IDocumentBulkRepository>();

    private BulkDeleteCommandHandler CreateSut(int maxItems = 50)
    {
        return new BulkDeleteCommandHandler(_repository, Options.Create(new BulkOptions { MaxItems = maxItems }));
    }

    private static Document Upload()
    {
        return Document.CreateUpload(10, "a.pdf", "application/pdf", null, "storage/a", DateTimeOffset.UtcNow).Value;
    }

    [Fact]
    public async Task Should_DeleteAll_When_EveryDocumentValid()
    {
        // Arrange
        Document a = Upload();
        Document b = Upload();
        _repository.GetByIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([a, b]);

        // Act
        BulkResult result = await CreateSut().Handle(new BulkDeleteCommand([a.Id, b.Id]), CancellationToken.None);

        // Assert
        result.Outcome.Should().Be(BulkOutcome.Success);
        await _repository.Received(1).DeleteAllAsync(
            Arg.Is<IReadOnlyList<Document>>(docs => docs.Count == 2), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_RejectAll_When_OneIdNotInSession()
    {
        // Arrange — `missing` is absent from the session-filtered read.
        Document a = Upload();
        var missing = Guid.NewGuid();
        _repository.GetByIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([a]);

        // Act
        BulkResult result = await CreateSut().Handle(new BulkDeleteCommand([a.Id, missing]), CancellationToken.None);

        // Assert — 422 failures, and NOTHING deleted.
        result.Outcome.Should().Be(BulkOutcome.ValidationFailed);
        result.Failures.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new BulkFailure(missing, "document.not_found"));
        await _repository.DidNotReceive().DeleteAllAsync(
            Arg.Any<IReadOnlyList<Document>>(), Arg.Any<CancellationToken>());
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
        BulkResult result = await CreateSut().Handle(new BulkDeleteCommand([a.Id, demo.Id]), CancellationToken.None);

        // Assert
        result.Outcome.Should().Be(BulkOutcome.ValidationFailed);
        result.Failures.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new BulkFailure(demo.Id, "document.read_only"));
        await _repository.DidNotReceive().DeleteAllAsync(
            Arg.Any<IReadOnlyList<Document>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_Deduplicate_BeforeDeleting()
    {
        // Arrange
        Document a = Upload();
        _repository.GetByIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([a]);

        // Act
        BulkResult result = await CreateSut().Handle(new BulkDeleteCommand([a.Id, a.Id]), CancellationToken.None);

        // Assert
        result.Outcome.Should().Be(BulkOutcome.Success);
        await _repository.Received(1).GetByIdsAsync(
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 1), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_ReturnBadRequest_When_ListEmpty()
    {
        // Act
        BulkResult result = await CreateSut().Handle(new BulkDeleteCommand([]), CancellationToken.None);

        // Assert
        result.Outcome.Should().Be(BulkOutcome.BadRequest);
        result.Error!.Code.Should().Be("document.bulk_empty");
    }

    [Fact]
    public async Task Should_ReturnBadRequest_When_ListOverCap()
    {
        // Arrange
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };

        // Act
        BulkResult result = await CreateSut(maxItems: 2).Handle(new BulkDeleteCommand(ids), CancellationToken.None);

        // Assert
        result.Outcome.Should().Be(BulkOutcome.BadRequest);
        result.Error!.Code.Should().Be("document.bulk_too_large");
    }
}
