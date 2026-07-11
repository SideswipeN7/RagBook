using System.Net;
using FluentAssertions;
using Xunit;

namespace RagBook.Api.IntegrationTests.Settings;

/// <summary>
/// Proves the full key never leaks across a save → status → delete flow (US-02 AC-5, FR-011, SC-005):
/// neither the captured application logs nor any response body contain the full key, and every settings
/// response carries <c>Cache-Control: no-store</c> (FR-013).
/// </summary>
public sealed class ApiKeyNoLeakTests(SettingsApiFactory factory) : IClassFixture<SettingsApiFactory>
{
    [Fact]
    public async Task Should_NeverLeakFullKey_Across_FullFlow()
    {
        // Arrange
        factory.Validator.NextResult = RagBook.Modules.Settings.Domain.ApiKeyValidationResult.Valid;
        factory.Logs.Clear();
        HttpClient client = SettingsTestClient.CreateClient(factory, Guid.NewGuid());

        // Act — exercise every settings endpoint with a known key.
        HttpResponseMessage post = await SettingsTestClient.PostKeyAsync(client, SettingsTestClient.ValidKey);
        HttpResponseMessage get = await client.GetAsync("/api/settings/api-key");
        HttpResponseMessage delete = await client.DeleteAsync("/api/settings/api-key");

        // Assert — no full key anywhere.
        string postBody = await post.Content.ReadAsStringAsync();
        string getBody = await get.Content.ReadAsStringAsync();
        postBody.Should().NotContain(SettingsTestClient.ValidKey);
        getBody.Should().NotContain(SettingsTestClient.ValidKey);
        factory.Logs.Messages.Should().NotContain(message => message.Contains(SettingsTestClient.ValidKey));

        // Assert — every response is no-store.
        post.Headers.CacheControl!.NoStore.Should().BeTrue();
        get.Headers.CacheControl!.NoStore.Should().BeTrue();
        delete.Headers.CacheControl!.NoStore.Should().BeTrue();
        delete.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}
