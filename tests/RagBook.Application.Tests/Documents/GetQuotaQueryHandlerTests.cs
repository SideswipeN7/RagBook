using FluentAssertions;
using NSubstitute;
using RagBook.Modules.Documents.Domain;
using RagBook.Modules.Documents.Features.GetQuota;
using Xunit;

namespace RagBook.Application.Tests.Documents;

public sealed class GetQuotaQueryHandlerTests
{
    private readonly IQuotaService _quotaService = Substitute.For<IQuotaService>();

    private GetQuotaQueryHandler CreateSut()
    {
        return new GetQuotaQueryHandler(_quotaService);
    }

    [Fact]
    public async Task Should_ReturnUsedCountAndMb_When_GettingQuotaState()
    {
        // Arrange (AC-1) — 7 documents totalling 12.3 MB against the free-tier limits.
        var limits = new QuotaLimits(MaxDocuments: 10, MaxFileSizeBytes: 10_000_000, MaxTotalBytes: 50_000_000);
        _quotaService.GetSnapshotAsync(Arg.Any<CancellationToken>())
            .Returns(new QuotaSnapshot(UsedDocuments: 7, UsedBytes: 12_300_000, limits));
        var sut = CreateSut();

        // Act
        QuotaStateResponse response = await sut.Handle(new GetQuotaQuery(), CancellationToken.None);

        // Assert
        response.UsedDocuments.Should().Be(7);
        response.MaxDocuments.Should().Be(10);
        response.UsedMb.Should().Be(12.3);
        response.MaxTotalMb.Should().Be(50);
        response.MaxFileSizeMb.Should().Be(10);
        response.CanUpload.Should().BeTrue();
    }
}
