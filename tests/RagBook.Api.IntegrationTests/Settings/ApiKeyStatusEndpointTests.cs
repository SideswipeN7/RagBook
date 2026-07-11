using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using RagBook.Modules.Settings.Domain;
using Xunit;

namespace RagBook.Api.IntegrationTests.Settings;

/// <summary>
/// Acceptance tests for <c>GET /api/settings/api-key</c> (US-02 AC-2, FR-007, FR-012). Status is
/// <c>none</c> or <c>active</c> + mask; the full key never appears; keys are isolated per session.
/// </summary>
public sealed class ApiKeyStatusEndpointTests(SettingsApiFactory factory) : IClassFixture<SettingsApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private async Task<ApiKeyStatusResponse> GetStatusAsync(HttpClient client)
    {
        HttpResponseMessage response = await client.GetAsync("/api/settings/api-key");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();

        return (await response.Content.ReadFromJsonAsync<ApiKeyStatusResponse>(JsonOptions))!;
    }

    [Fact]
    public async Task Should_ReturnNone_When_NoKeyStored()
    {
        // Arrange
        HttpClient client = SettingsTestClient.CreateClient(factory, Guid.NewGuid());

        // Act
        ApiKeyStatusResponse status = await GetStatusAsync(client);

        // Assert
        status.Status.Should().Be("none");
        status.MaskedKey.Should().BeNull();
    }

    [Fact]
    public async Task Should_ReturnActiveMaskOnly_Never_FullKey_After_Save()
    {
        // Arrange
        factory.Validator.NextResult = ApiKeyValidationResult.Valid;
        HttpClient client = SettingsTestClient.CreateClient(factory, Guid.NewGuid());
        await SettingsTestClient.PostKeyAsync(client, SettingsTestClient.ValidKey);

        // Act
        HttpResponseMessage response = await client.GetAsync("/api/settings/api-key");
        string rawBody = await response.Content.ReadAsStringAsync();
        var status = JsonSerializer.Deserialize<ApiKeyStatusResponse>(rawBody, JsonOptions)!;

        // Assert
        status.Status.Should().Be("active");
        status.MaskedKey.Should().Be("sk-ant-api03-…B7fA");
        rawBody.Should().NotContain(SettingsTestClient.ValidKey);
    }

    [Fact]
    public async Task Should_IsolateKey_BetweenSessions()
    {
        // Arrange — session A stores a key.
        factory.Validator.NextResult = ApiKeyValidationResult.Valid;
        HttpClient sessionA = SettingsTestClient.CreateClient(factory, Guid.NewGuid());
        await SettingsTestClient.PostKeyAsync(sessionA, SettingsTestClient.ValidKey);

        // Act — session B reads status.
        HttpClient sessionB = SettingsTestClient.CreateClient(factory, Guid.NewGuid());
        ApiKeyStatusResponse statusB = await GetStatusAsync(sessionB);

        // Assert — B never sees A's key (FR-012).
        statusB.Status.Should().Be("none");
        statusB.MaskedKey.Should().BeNull();
    }
}
