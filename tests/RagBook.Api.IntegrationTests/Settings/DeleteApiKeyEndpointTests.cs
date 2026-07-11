using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using RagBook.Modules.Settings.Domain;
using Xunit;

namespace RagBook.Api.IntegrationTests.Settings;

/// <summary>
/// Acceptance tests for <c>DELETE /api/settings/api-key</c> (US-02 AC-4). Delete returns the session to
/// <c>none</c> and is idempotent (a second delete still succeeds).
/// </summary>
public sealed class DeleteApiKeyEndpointTests(SettingsApiFactory factory) : IClassFixture<SettingsApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Should_Delete_Then_StatusNone_And_SecondDeleteStillNoContent()
    {
        // Arrange — store a key first.
        factory.Validator.NextResult = ApiKeyValidationResult.Valid;
        HttpClient client = SettingsTestClient.CreateClient(factory, Guid.NewGuid());
        await SettingsTestClient.PostKeyAsync(client, SettingsTestClient.ValidKey);

        // Act — delete, check status, delete again.
        HttpResponseMessage firstDelete = await client.DeleteAsync("/api/settings/api-key");
        ApiKeyStatusResponse afterDelete =
            (await (await client.GetAsync("/api/settings/api-key")).Content.ReadFromJsonAsync<ApiKeyStatusResponse>(JsonOptions))!;
        HttpResponseMessage secondDelete = await client.DeleteAsync("/api/settings/api-key");

        // Assert
        firstDelete.StatusCode.Should().Be(HttpStatusCode.NoContent);
        firstDelete.Headers.CacheControl!.NoStore.Should().BeTrue();
        afterDelete.Status.Should().Be("none");
        secondDelete.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}
