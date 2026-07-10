using System.Net;
using System.Text;
using FluentAssertions;
using RagBook.Api.IntegrationTests.Folders;
using Xunit;

namespace RagBook.Api.IntegrationTests.Documents;

/// <summary>
/// Concurrency guarantees for upload: the per-session advisory lock serializes a session's uploads, so
/// duplicate names get distinct suffixes (AC-5) and the count quota admits at most one at the boundary
/// (FR-007), even under simultaneous requests.
/// </summary>
public sealed class UploadConcurrencyTests(RagBookApiFactory factory) : IClassFixture<RagBookApiFactory>
{
    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.7\nx\n");

    [Fact]
    public async Task Should_AvoidCollision_When_TwoDuplicateUploadsRace()
    {
        // Arrange (AC-5)
        var sessionId = Guid.NewGuid();
        var folder = await new FolderApiClient(factory, sessionId).CreateAsync("Umowy", null);
        var client = new DocumentApiClient(factory, sessionId);

        // Act — two identical uploads into the same folder at the same time.
        var first = client.UploadAsync(Pdf, "umowa.pdf", "application/pdf", folder.Id);
        var second = client.UploadAsync(Pdf, "umowa.pdf", "application/pdf", folder.Id);
        var results = await Task.WhenAll(first, second);

        // Assert — both admitted, with two distinct names (base + "(1)").
        results.Should().OnlyContain(r => r.Status == HttpStatusCode.Created);
        var names = results.Select(r => r.Document!.FileName).OrderBy(n => n).ToArray();
        names.Should().BeEquivalentTo(["umowa (1).pdf", "umowa.pdf"]);
    }

    [Fact]
    public async Task Should_AdmitAtMostOne_When_TwoUploadsRaceAtLimit()
    {
        // Arrange (FR-007) — fill to one below the 10-document limit.
        var sessionId = Guid.NewGuid();
        var client = new DocumentApiClient(factory, sessionId);
        for (int i = 0; i < 9; i++)
        {
            (await client.UploadAsync(Pdf, $"doc{i}.pdf", "application/pdf", null)).Status.Should().Be(HttpStatusCode.Created);
        }

        // Act — two uploads race for the last slot.
        var results = await Task.WhenAll(
            client.UploadAsync(Pdf, "a.pdf", "application/pdf", null),
            client.UploadAsync(Pdf, "b.pdf", "application/pdf", null));

        // Assert — at most one admitted; the total never exceeds 10.
        results.Count(r => r.Status == HttpStatusCode.Created).Should().BeLessThanOrEqualTo(1);
        factory.CountStoredBlobs(sessionId).Should().BeLessThanOrEqualTo(10);
    }
}
