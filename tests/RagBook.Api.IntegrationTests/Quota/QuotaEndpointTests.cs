using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using RagBook.Modules.Documents.Domain;
using RagBook.Modules.Documents.Features.GetQuota;
using Xunit;

namespace RagBook.Api.IntegrationTests.Quota;

/// <summary>
/// Acceptance tests for <c>GET /api/quota</c> against the real host + Dockerized PostgreSQL. Documents
/// are seeded for a chosen session; the request carries that session's cookie so the endpoint reports
/// its usage (AC-1), excluding demo documents (FR-007) and reflecting deletions (AC-4).
/// </summary>
public sealed class QuotaEndpointTests(RagBookApiFactory factory) : IClassFixture<RagBookApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private async Task<QuotaStateResponse> GetQuotaAsync(Guid sessionId)
    {
        // The session cookie is issued Secure, so ride https; send it explicitly to bind the request
        // to the seeded session.
        var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = false,
        });
        client.DefaultRequestHeaders.Add("Cookie", $"ragbook_session={sessionId}");

        var response = await client.GetAsync("/api/quota");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        return (await response.Content.ReadFromJsonAsync<QuotaStateResponse>(JsonOptions))!;
    }

    [Fact]
    public async Task Should_ReportCountAndUsage_When_SessionHasDocuments()
    {
        // Arrange (AC-1) — 7 documents totalling 12,300,000 bytes (12.3 MB).
        var sessionId = Guid.NewGuid();
        await QuotaSeed.SeedAsync(
            factory,
            sessionId,
            QuotaSeed.Doc.User(2_000_000),
            QuotaSeed.Doc.User(2_000_000),
            QuotaSeed.Doc.User(2_000_000),
            QuotaSeed.Doc.User(2_000_000),
            QuotaSeed.Doc.User(2_000_000),
            QuotaSeed.Doc.User(2_000_000),
            QuotaSeed.Doc.User(300_000));

        // Act
        QuotaStateResponse state = await GetQuotaAsync(sessionId);

        // Assert
        state.UsedDocuments.Should().Be(7);
        state.MaxDocuments.Should().Be(10);
        state.UsedMb.Should().Be(12.3);
        state.MaxTotalMb.Should().Be(50);
        state.MaxFileSizeMb.Should().Be(10);
        state.CanUpload.Should().BeTrue();
    }

    [Fact]
    public async Task Should_ExcludeDemoDocuments_When_CountingQuota()
    {
        // Arrange (FR-007) — 2 user docs (one Failed) count; 1 demo doc is excluded.
        var sessionId = Guid.NewGuid();
        await QuotaSeed.SeedAsync(
            factory,
            sessionId,
            new QuotaSeed.Doc(1_000_000, DocumentOrigin.User, DocumentStatus.Processing),
            new QuotaSeed.Doc(1_000_000, DocumentOrigin.User, DocumentStatus.Failed),
            new QuotaSeed.Doc(5_000_000, DocumentOrigin.Demo, DocumentStatus.Ready));

        // Act
        QuotaStateResponse state = await GetQuotaAsync(sessionId);

        // Assert — demo excluded, failed included: 2 docs / 2.0 MB.
        state.UsedDocuments.Should().Be(2);
        state.UsedMb.Should().Be(2);
    }

    [Fact]
    public async Task Should_ReflectFreedSlot_When_DocumentRemoved()
    {
        // Arrange (AC-4) — fill to the document-count limit.
        var sessionId = Guid.NewGuid();
        var documents = Enumerable.Range(0, 10).Select(_ => QuotaSeed.Doc.User(100_000)).ToArray();
        await QuotaSeed.SeedAsync(factory, sessionId, documents);

        QuotaStateResponse full = await GetQuotaAsync(sessionId);
        full.UsedDocuments.Should().Be(10);
        full.CanUpload.Should().BeFalse();

        // Act — a deletion frees a slot (US-08 stand-in).
        await QuotaSeed.RemoveOneAsync(factory, sessionId);
        QuotaStateResponse afterDelete = await GetQuotaAsync(sessionId);

        // Assert — the counter drops and uploading is possible again, no page reload involved.
        afterDelete.UsedDocuments.Should().Be(9);
        afterDelete.CanUpload.Should().BeTrue();
    }

    [Fact]
    public async Task Should_ReportEmptyQuota_When_SessionIsFresh()
    {
        // Arrange — a session with no documents.
        var sessionId = Guid.NewGuid();

        // Act
        QuotaStateResponse state = await GetQuotaAsync(sessionId);

        // Assert
        state.UsedDocuments.Should().Be(0);
        state.UsedMb.Should().Be(0);
        state.CanUpload.Should().BeTrue();
    }
}
