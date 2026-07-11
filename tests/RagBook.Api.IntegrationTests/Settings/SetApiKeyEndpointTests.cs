using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using RagBook.Modules.Settings.Domain;
using Xunit;

namespace RagBook.Api.IntegrationTests.Settings;

/// <summary>
/// Acceptance tests for <c>POST /api/settings/api-key</c> against the real host with a faked validator
/// (US-02 AC-1). Covers the happy path plus the distinct rejection / unavailable / throttled outcomes,
/// and asserts the <c>no-store</c> header.
/// </summary>
public sealed class SetApiKeyEndpointTests(SettingsApiFactory factory) : IClassFixture<SettingsApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Should_StoreAndReturnActiveMask_When_ValidKey()
    {
        // Arrange
        factory.Validator.NextResult = ApiKeyValidationResult.Valid;
        HttpClient client = SettingsTestClient.CreateClient(factory, Guid.NewGuid());

        // Act
        HttpResponseMessage response = await SettingsTestClient.PostKeyAsync(client, SettingsTestClient.ValidKey);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();

        var body = (await response.Content.ReadFromJsonAsync<ApiKeyStatusResponse>(JsonOptions))!;
        body.Status.Should().Be("active");
        body.MaskedKey.Should().Be("sk-ant-api03-…B7fA");
        (await response.Content.ReadAsStringAsync()).Should().NotContain(SettingsTestClient.ValidKey);
    }

    [Fact]
    public async Task Should_Return400_When_ProviderRejects()
    {
        // Arrange
        factory.Validator.NextResult = ApiKeyValidationResult.Rejected;
        HttpClient client = SettingsTestClient.CreateClient(factory, Guid.NewGuid());

        // Act
        HttpResponseMessage response = await SettingsTestClient.PostKeyAsync(client, SettingsTestClient.ValidKey);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await SettingsTestClient.ReadProblemCodeAsync(response)).Should().Be("settings.invalid_api_key");
    }

    [Fact]
    public async Task Should_Return503_When_ValidationUnavailable()
    {
        // Arrange
        factory.Validator.NextResult = ApiKeyValidationResult.Unavailable;
        HttpClient client = SettingsTestClient.CreateClient(factory, Guid.NewGuid());

        // Act
        HttpResponseMessage response = await SettingsTestClient.PostKeyAsync(client, SettingsTestClient.ValidKey);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await SettingsTestClient.ReadProblemCodeAsync(response)).Should().Be("settings.validation_unavailable");
    }

    [Fact]
    public async Task Should_Return429_When_ThrottleExceeded()
    {
        // Arrange — the default limit is 5 attempts per window per session.
        factory.Validator.NextResult = ApiKeyValidationResult.Valid;
        HttpClient client = SettingsTestClient.CreateClient(factory, Guid.NewGuid());

        // Act — the 6th attempt from the same session trips the throttle.
        HttpResponseMessage? throttled = null;
        for (int attempt = 0; attempt < 6; attempt++)
        {
            throttled = await SettingsTestClient.PostKeyAsync(client, SettingsTestClient.ValidKey);
        }

        // Assert
        throttled!.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await SettingsTestClient.ReadProblemCodeAsync(throttled)).Should().Be("settings.too_many_attempts");
    }
}
