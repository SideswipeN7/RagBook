using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using RagBook.Api.IntegrationTests.Chat;
using Xunit;

namespace RagBook.Api.IntegrationTests.Demo;

/// <summary>
/// Acceptance tests for US-03 demo mode against the real host + pgvector, with the scriptable fake generator and a
/// configured application key. Proves: idempotent seeding; cross-session demo visibility; a keyless demo ask that
/// streams a grounded answer on the application key; the per-session question limit (429); demo exclusion from
/// quota; and that a user session cannot mutate a global demo document.
/// </summary>
public sealed class DemoModeEndpointTests(DemoApiFactory factory) : IClassFixture<DemoApiFactory>
{
    private static readonly JsonSerializerOptions JsonWeb = new(JsonSerializerDefaults.Web);

    private async Task EnsureSeededAsync() => await factory.SeedAsync();

    [Fact]
    public async Task Should_SeedDemoDocuments_Idempotently()
    {
        // Act — seeding twice must not duplicate (fixed ids).
        await factory.SeedAsync();
        int afterSecondRun = await factory.SeedAsync();

        // Assert — exactly the two manifest documents exist.
        afterSecondRun.Should().Be(2);
    }

    [Fact]
    public async Task Should_ExposeDemoDocuments_InEverySession()
    {
        // Arrange
        await EnsureSeededAsync();
        HttpClient client = factory.CreateSessionClient(Guid.NewGuid()); // a fresh session that seeded nothing

        // Act
        using JsonDocument tree = JsonDocument.Parse(await (await client.GetAsync("/api/tree")).Content.ReadAsStringAsync());

        // Assert — the session's own documents are empty, but the global demo list is visible.
        JsonElement demo = tree.RootElement.GetProperty("demo");
        demo.GetArrayLength().Should().Be(2);
        tree.RootElement.GetProperty("documents").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Should_AnswerDemoQuestion_WithoutASessionKey()
    {
        // Arrange — a fresh session with NO API key set.
        await EnsureSeededAsync();
        factory.Generator.Reset();
        HttpClient client = SseEvents.CreateClient(factory, Guid.NewGuid());

        // Act
        HttpResponseMessage response = await SseEvents.AskAsync(client, "Czego dotycza dokumenty demo?", "demo");
        IReadOnlyList<SseEvents.Event> events = await SseEvents.ReadAsync(response);

        // Assert — a full grounded stream on the application key; the pipeline flagged the context demo.
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        events.Should().Contain(e => e.Name == "sources");
        events.Count(e => e.Name == "token").Should().BeGreaterThanOrEqualTo(1);
        events[^1].Name.Should().Be("done");
        factory.Generator.Invoked.Should().BeTrue();
        factory.Generator.LastContext!.IsDemo.Should().BeTrue();
    }

    [Fact]
    public async Task Should_RefuseFurtherDemoQuestions_When_SessionLimitReached()
    {
        // Arrange — the factory caps the session at 3 demo questions.
        await EnsureSeededAsync();
        factory.Generator.Reset();
        var session = Guid.NewGuid();
        HttpClient client = SseEvents.CreateClient(factory, session);

        // Act — three demo asks within the limit, then a fourth over it.
        for (int i = 0; i < 3; i++)
        {
            (await SseEvents.AskAsync(client, "pytanie demo", "demo")).StatusCode.Should().Be(HttpStatusCode.OK);
        }

        HttpResponseMessage over = await SseEvents.AskAsync(client, "pytanie demo", "demo");

        // Assert — 429 with the stable demo-limit code, and the status reports 0 remaining.
        over.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await CodeOf(over)).Should().Be("chat.demo_limit_reached");

        using JsonDocument status = JsonDocument.Parse(await (await client.GetAsync("/api/demo/status")).Content.ReadAsStringAsync());
        status.RootElement.GetProperty("remaining").GetInt32().Should().Be(0);
        status.RootElement.GetProperty("available").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Should_ExcludeDemoDocuments_FromUserQuota()
    {
        // Arrange
        await EnsureSeededAsync();
        HttpClient client = factory.CreateSessionClient(Guid.NewGuid());

        // Act
        using JsonDocument quota = JsonDocument.Parse(await (await client.GetAsync("/api/quota")).Content.ReadAsStringAsync());

        // Assert — the demo documents do not count toward the user's document quota.
        quota.RootElement.GetProperty("usedDocuments").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task Should_NotLetAUserSessionMutateADemoDocument()
    {
        // Arrange — a demo document is owned by the sentinel demo session, invisible to a user session.
        await EnsureSeededAsync();
        HttpClient client = factory.CreateSessionClient(Guid.NewGuid());

        // Act — a user attempts to delete a global demo document.
        HttpResponseMessage delete = await client.DeleteAsync($"/api/documents/{DemoApiFactory.DemoDocA}");

        // Assert — blocked (not found, since it is not the user's) and still present in the demo list.
        delete.StatusCode.Should().Be(HttpStatusCode.NotFound);
        using JsonDocument tree = JsonDocument.Parse(await (await client.GetAsync("/api/tree")).Content.ReadAsStringAsync());
        tree.RootElement.GetProperty("demo").GetArrayLength().Should().Be(2);
    }

    private static async Task<string?> CodeOf(HttpResponseMessage response)
    {
        using JsonDocument problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return problem.RootElement.TryGetProperty("code", out JsonElement value) ? value.GetString() : null;
    }
}

/// <summary>The per-IP hourly demo limit (US-03 AC-3) — its own factory with a tiny IP cap.</summary>
public sealed class DemoIpRateLimitTests(DemoIpLimitApiFactory factory) : IClassFixture<DemoIpLimitApiFactory>
{
    [Fact]
    public async Task Should_Return429WithRetryAfter_When_IpHourlyLimitExceeded()
    {
        // Arrange — the factory caps this IP at 2 demo requests/hour (session limit is large, so IP trips first).
        await factory.SeedAsync();
        factory.Generator.Reset();
        HttpClient client = SseEvents.CreateClient(factory, Guid.NewGuid());

        // Act — two allowed, the third over the IP limit.
        (await SseEvents.AskAsync(client, "pytanie demo", "demo")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await SseEvents.AskAsync(client, "pytanie demo", "demo")).StatusCode.Should().Be(HttpStatusCode.OK);
        HttpResponseMessage over = await SseEvents.AskAsync(client, "pytanie demo", "demo");

        // Assert — 429 with a Retry-After header pointing at the window reset.
        over.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        over.Headers.RetryAfter.Should().NotBeNull();
    }
}
