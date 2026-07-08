using FluentAssertions;
using RagBook.Modules.Documents.Domain;
using RagBook.Shared.Results;
using Xunit;

namespace RagBook.Domain.Tests.Documents;

public sealed class QuotaSnapshotTests
{
    // 10 documents, 10 MB per file, 50 MB total — the free-tier defaults, in bytes.
    private static readonly QuotaLimits Limits = new(MaxDocuments: 10, MaxFileSizeBytes: 10_000_000, MaxTotalBytes: 50_000_000);

    private static QuotaSnapshot Snapshot(int usedDocuments, long usedBytes)
    {
        return new QuotaSnapshot(usedDocuments, usedBytes, Limits);
    }

    [Fact]
    public void Should_ReturnQuotaExceeded_When_DocumentCountAtLimit()
    {
        // Arrange (AC-2) — 10 of 10 documents, plenty of storage headroom.
        var snapshot = Snapshot(usedDocuments: 10, usedBytes: 1_000_000);

        // Act
        Result result = snapshot.CanAdmit(fileSizeBytes: 1_000);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("quota.exceeded");
    }

    [Fact]
    public void Should_ReturnTotalSizeExceeded_When_AddingFileWouldCrossTotal()
    {
        // Arrange (AC-3) — 45 MB used against a 50 MB limit, uploading 8 MB.
        var snapshot = Snapshot(usedDocuments: 5, usedBytes: 45_000_000);

        // Act
        Result result = snapshot.CanAdmit(fileSizeBytes: 8_000_000);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("quota.total_size_exceeded");
        result.Error.Message.Should().Contain("5"); // remaining ~5 MB conveyed to the user
    }

    [Fact]
    public void Should_Admit_When_UsageExactlyAtTotalBoundary()
    {
        // Arrange — 42 MB used, uploading exactly 8 MB → lands on 50 MB (the limit), not over it.
        var snapshot = Snapshot(usedDocuments: 5, usedBytes: 42_000_000);

        // Act
        Result result = snapshot.CanAdmit(fileSizeBytes: 8_000_000);

        // Assert — only crossing the limit is rejected; exactly-at-limit is admitted.
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Should_ReturnFileTooLarge_When_FileExceedsPerFileMax()
    {
        // Arrange — a single 11 MB file against a 10 MB per-file limit, session otherwise empty.
        var snapshot = Snapshot(usedDocuments: 0, usedBytes: 0);

        // Act
        Result result = snapshot.CanAdmit(fileSizeBytes: 11_000_000);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("quota.file_too_large");
    }

    [Fact]
    public void Should_Admit_When_WithinAllLimits()
    {
        // Arrange
        var snapshot = Snapshot(usedDocuments: 7, usedBytes: 12_000_000);

        // Act
        Result result = snapshot.CanAdmit(fileSizeBytes: 3_000_000);

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Should_ReportUsedMbAndFullState_When_Projected()
    {
        // Arrange — 12,300,000 bytes → 12.3 MB; below both limits.
        var snapshot = Snapshot(usedDocuments: 7, usedBytes: 12_300_000);

        // Act & Assert
        snapshot.UsedMb.Should().Be(12.3);
        snapshot.MaxTotalMb.Should().Be(50);
        snapshot.IsFull.Should().BeFalse();
        snapshot.CanUpload.Should().BeTrue();
    }

    [Fact]
    public void Should_KeepExistingUsage_When_LimitLoweredBelowCurrentUsage()
    {
        // Arrange (FR-009) — usage of 8 documents against a lowered limit of 5 documents.
        var loweredLimits = new QuotaLimits(MaxDocuments: 5, MaxFileSizeBytes: 10_000_000, MaxTotalBytes: 50_000_000);
        var snapshot = new QuotaSnapshot(UsedDocuments: 8, UsedBytes: 20_000_000, loweredLimits);

        // Act — the snapshot only decides admission; it never mutates usage.
        Result result = snapshot.CanAdmit(fileSizeBytes: 1_000);

        // Assert — existing usage is unchanged; new uploads are blocked until it drops below the limit.
        snapshot.UsedDocuments.Should().Be(8);
        snapshot.IsFull.Should().BeTrue();
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("quota.exceeded");
    }
}
