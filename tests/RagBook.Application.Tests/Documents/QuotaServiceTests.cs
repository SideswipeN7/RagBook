using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using RagBook.Modules.Documents.Domain;
using RagBook.Modules.Documents.Quota;
using RagBook.Shared.Results;
using Xunit;

namespace RagBook.Application.Tests.Documents;

public sealed class QuotaServiceTests
{
    private readonly IDocumentQuotaRepository _repository = Substitute.For<IDocumentQuotaRepository>();

    private QuotaService CreateSut(QuotaOptions? options = null)
    {
        return new QuotaService(_repository, Options.Create(options ?? new QuotaOptions()));
    }

    [Fact]
    public async Task Should_ProjectSnapshot_When_GettingState()
    {
        // Arrange
        _repository.CountAsync(Arg.Any<CancellationToken>()).Returns(7);
        _repository.SumSizeBytesAsync(Arg.Any<CancellationToken>()).Returns(12_300_000L);
        var sut = CreateSut();

        // Act
        QuotaSnapshot snapshot = await sut.GetSnapshotAsync(CancellationToken.None);

        // Assert — defaults are 10 / 10 MB / 50 MB.
        snapshot.UsedDocuments.Should().Be(7);
        snapshot.UsedBytes.Should().Be(12_300_000);
        snapshot.Limits.MaxDocuments.Should().Be(10);
        snapshot.Limits.MaxTotalBytes.Should().Be(50_000_000);
    }

    [Fact]
    public async Task Should_ReturnQuotaExceeded_When_CheckCanUploadAtCountLimit()
    {
        // Arrange (AC-2)
        _repository.CountAsync(Arg.Any<CancellationToken>()).Returns(10);
        _repository.SumSizeBytesAsync(Arg.Any<CancellationToken>()).Returns(1_000_000L);
        var sut = CreateSut();

        // Act
        Result result = await sut.CheckCanUpload(fileSizeBytes: 1_000, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("quota.exceeded");
    }

    [Fact]
    public async Task Should_ReturnTotalSizeExceeded_When_CheckCanUploadWouldCrossTotal()
    {
        // Arrange (AC-3) — 45 MB used, uploading 8 MB against 50 MB.
        _repository.CountAsync(Arg.Any<CancellationToken>()).Returns(5);
        _repository.SumSizeBytesAsync(Arg.Any<CancellationToken>()).Returns(45_000_000L);
        var sut = CreateSut();

        // Act
        Result result = await sut.CheckCanUpload(fileSizeBytes: 8_000_000, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("quota.total_size_exceeded");
    }

    [Fact]
    public async Task Should_DelegateToRepository_When_AdmittingWithinQuota()
    {
        // Arrange
        _repository.TryAddWithinQuotaAsync(Arg.Any<Document>(), Arg.Any<QuotaLimits>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        var sut = CreateSut();

        // Act
        Result<Guid> result = await sut.TryAdmitAsync(fileSizeBytes: 1_000, DocumentOrigin.User, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBe(Guid.Empty);
        await _repository.Received(1).TryAddWithinQuotaAsync(
            Arg.Is<Document>(document => document.SizeBytes == 1_000 && document.Origin == DocumentOrigin.User),
            Arg.Any<QuotaLimits>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_ReturnFailureAndNotInsert_When_AdmitRejectedByRepository()
    {
        // Arrange — the atomic admit reports the session is full.
        _repository.TryAddWithinQuotaAsync(Arg.Any<Document>(), Arg.Any<QuotaLimits>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure(RagBook.Modules.Documents.Errors.QuotaErrors.QuotaExceeded));
        var sut = CreateSut();

        // Act
        Result<Guid> result = await sut.TryAdmitAsync(fileSizeBytes: 1_000, DocumentOrigin.User, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("quota.exceeded");
    }
}
