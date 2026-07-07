using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using RagBook.API.Endpoints;
using RagBook.Modules.Session.Features;
using Xunit;

namespace RagBook.Api.IntegrationTests;

/// <summary>
/// End-to-end acceptance tests for US-01 against the real host + Dockerized PostgreSQL. Each
/// <see cref="HttpClient"/> has its own cookie jar, so it represents a distinct anonymous session.
/// </summary>
public sealed class SessionIsolationTests(RagBookApiFactory factory)
    : IClassFixture<RagBookApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private HttpClient NewSessionClient()
    {
        // The session cookie is issued Secure (AC-1), so it only round-trips over https — model a real
        // secure client, otherwise CookieContainer drops it and every request mints a fresh session.
        return factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = true,
        });
    }

    private static async Task<Guid> CreateResourceAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync("/api/resources", new CreateResourceRequest(name));
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var created = await response.Content.ReadFromJsonAsync<CreateResourceResponse>(JsonOptions);

        return created!.Id;
    }

    [Fact]
    public async Task Should_IssueSessionCookie_When_RequestHasNoCookie()
    {
        // Arrange
        var client = NewSessionClient();

        // Act
        var response = await client.GetAsync("/api/session");

        // Assert (AC-1)
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.TryGetValues("Set-Cookie", out var cookies).Should().BeTrue();
        var setCookie = string.Join(";", cookies!).ToLowerInvariant();
        setCookie.Should().Contain("ragbook_session=");
        setCookie.Should().Contain("httponly");
        setCookie.Should().Contain("secure");
        setCookie.Should().Contain("samesite=strict");

        var state = await response.Content.ReadFromJsonAsync<SessionStateResponse>(JsonOptions);
        state!.IsNew.Should().BeTrue();
        state.ResourceCount.Should().Be(0);
    }

    [Fact]
    public async Task Should_ReturnOwnResources_When_ReturningWithSameCookie()
    {
        // Arrange (AC-2)
        var client = NewSessionClient();
        var id = await CreateResourceAsync(client, "persisted");

        // Act — a subsequent request on the same cookie jar sees the resource.
        var listed = await client.GetFromJsonAsync<List<ResourceResponse>>("/api/resources", JsonOptions);

        // Assert
        listed.Should().ContainSingle(resource => resource.Id == id);
    }

    [Fact]
    public async Task Should_Return404_When_RequestingAnotherSessionsResourceById()
    {
        // Arrange (AC-3)
        var owner = NewSessionClient();
        var other = NewSessionClient();
        var id = await CreateResourceAsync(owner, "owned-by-a");

        // Act — establish 'other' as its own session, then reach for A's resource by id.
        await other.GetAsync("/api/session");
        var response = await other.GetAsync($"/api/resources/{id}");

        // Assert — 404, never 403; existence is not disclosed.
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("code").GetString().Should().Be("session.resource_not_found");
    }

    [Fact]
    public async Task Should_NotListAnotherSessionsResources_When_Listing()
    {
        // Arrange (AC-3)
        var owner = NewSessionClient();
        var other = NewSessionClient();
        var id = await CreateResourceAsync(owner, "owned-by-a");

        // Act
        var otherList = await other.GetFromJsonAsync<List<ResourceResponse>>("/api/resources", JsonOptions);

        // Assert — A's resource never appears in B's list.
        otherList.Should().NotContain(resource => resource.Id == id);
    }

    [Fact]
    public async Task Should_ExcludeOtherSessionRows_When_TwoSessionsCreateResources()
    {
        // Arrange (AC-4 behavioral: the shared filter scopes every read)
        var sessionA = NewSessionClient();
        var sessionB = NewSessionClient();
        await CreateResourceAsync(sessionA, "a-1");
        await CreateResourceAsync(sessionA, "a-2");
        await CreateResourceAsync(sessionB, "b-1");

        // Act
        var listA = await sessionA.GetFromJsonAsync<List<ResourceResponse>>("/api/resources", JsonOptions);
        var listB = await sessionB.GetFromJsonAsync<List<ResourceResponse>>("/api/resources", JsonOptions);

        // Assert — each session sees only its own rows.
        listA.Should().HaveCount(2);
        listA!.Select(resource => resource.Name).Should().BeEquivalentTo(["a-1", "a-2"]);
        listB.Should().ContainSingle(resource => resource.Name == "b-1");
    }
}
