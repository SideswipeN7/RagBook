using System.Net;
using FluentAssertions;
using Xunit;

namespace RagBook.Api.IntegrationTests.Folders;

/// <summary>
/// Proves the per-parent name uniqueness guarantee holds under concurrency (AC-3 / FR-005): two
/// identical creates in the same parent, fired together, admit at most one — the database's partial
/// unique index on <c>LOWER(name)</c> catches the race and the loser is mapped to
/// <c>folder.duplicate_name</c>, never a naked 500.
/// </summary>
public sealed class FolderConcurrencyTests(RagBookApiFactory factory) : IClassFixture<RagBookApiFactory>
{
    [Fact]
    public async Task Should_AdmitAtMostOne_When_TwoIdenticalCreatesRaceInSameParent()
    {
        // Arrange
        var client = new FolderApiClient(factory, Guid.NewGuid());

        // Act — two identical root creates at the same time.
        Task<(HttpStatusCode Status, Guid? Id, string? Code)> first = client.CreateAsync("Umowy", null);
        Task<(HttpStatusCode Status, Guid? Id, string? Code)> second = client.CreateAsync("Umowy", null);
        var results = await Task.WhenAll(first, second);

        // Assert — exactly one Created, exactly one Conflict with the duplicate-name code.
        results.Count(r => r.Status == HttpStatusCode.Created).Should().Be(1);
        var loser = results.Single(r => r.Status != HttpStatusCode.Created);
        loser.Status.Should().Be(HttpStatusCode.Conflict);
        loser.Code.Should().Be("folder.duplicate_name");

        (await client.ListAsync()).Should().HaveCount(1);
    }
}
