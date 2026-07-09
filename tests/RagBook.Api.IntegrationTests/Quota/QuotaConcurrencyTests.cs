using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using RagBook.Modules.Documents.Domain;
using RagBook.Shared.Results;
using RagBook.Shared.Sessions;
using Xunit;

namespace RagBook.Api.IntegrationTests.Quota;

/// <summary>
/// Proves AC-5 against the real host + Dockerized PostgreSQL: the quota check and the insert are
/// atomic, so two concurrent uploads at the boundary admit at most one. Each admit runs in its own DI
/// scope (its own <c>DbContext</c>/connection), initialised to the same session; the transaction-scoped
/// advisory lock serialises them.
/// </summary>
public sealed class QuotaConcurrencyTests(RagBookApiFactory factory) : IClassFixture<RagBookApiFactory>
{
    private async Task<Result<Guid>> AdmitAsync(Guid sessionId, long sizeBytes)
    {
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ISessionInitializer>().Initialize(sessionId);
        var quotaService = scope.ServiceProvider.GetRequiredService<IQuotaService>();

        return await quotaService.TryAdmitAsync(sizeBytes, DocumentOrigin.User, CancellationToken.None);
    }

    [Fact]
    public async Task Should_AdmitAtMostOneDocument_When_TwoUploadsRaceAtLimit()
    {
        // Arrange (AC-5) — 9 of 10 documents; two uploads arrive at the same instant.
        var sessionId = Guid.NewGuid();
        var nineDocuments = Enumerable.Range(0, 9).Select(_ => QuotaSeed.Doc.User(1_000_000)).ToArray();
        await QuotaSeed.SeedAsync(factory, sessionId, nineDocuments);

        // Act — race two admits.
        Result<Guid>[] results = await Task.WhenAll(
            AdmitAsync(sessionId, 1_000_000),
            AdmitAsync(sessionId, 1_000_000));

        // Assert — at most one wins; the final count never exceeds the limit.
        results.Count(result => result.IsSuccess).Should().Be(1);
        results.Count(result => result.IsFailure).Should().Be(1);
        results.Single(result => result.IsFailure).Error.Code.Should().Be("quota.exceeded");

        int finalCount = await QuotaSeed.CountAsync(factory, sessionId);
        finalCount.Should().Be(10);
    }

    [Fact]
    public async Task Should_RejectAdmitAndNotInsert_When_CountAlreadyAtLimit()
    {
        // Arrange (AC-2 at the persistence boundary) — already at the document limit.
        var sessionId = Guid.NewGuid();
        var tenDocuments = Enumerable.Range(0, 10).Select(_ => QuotaSeed.Doc.User(1_000_000)).ToArray();
        await QuotaSeed.SeedAsync(factory, sessionId, tenDocuments);

        // Act
        Result<Guid> result = await AdmitAsync(sessionId, 1_000_000);

        // Assert — rejected with the quota code, nothing inserted.
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("quota.exceeded");
        (await QuotaSeed.CountAsync(factory, sessionId)).Should().Be(10);
    }
}
