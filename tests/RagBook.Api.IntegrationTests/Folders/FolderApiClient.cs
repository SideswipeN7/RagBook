using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using RagBook.Modules.Folders.Features.ListFolders;

namespace RagBook.Api.IntegrationTests.Folders;

/// <summary>
/// Drives the folder endpoints for a chosen session. The session cookie is issued <c>Secure</c>, so
/// requests ride https and carry the cookie explicitly to bind them to the seeded session — exactly
/// as the quota tests do.
/// </summary>
internal sealed class FolderApiClient(RagBookApiFactory factory, Guid sessionId)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private HttpClient CreateClient()
    {
        var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = false,
        });
        client.DefaultRequestHeaders.Add("Cookie", $"ragbook_session={sessionId}");

        return client;
    }

    public async Task<(HttpStatusCode Status, Guid? Id, string? Code)> CreateAsync(string name, Guid? parentId)
    {
        using HttpClient client = CreateClient();
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/folders",
            new { name, parentId },
            JsonOptions);

        if (response.StatusCode == HttpStatusCode.Created)
        {
            var body = await response.Content.ReadFromJsonAsync<CreatedBody>(JsonOptions);

            return (response.StatusCode, body!.Id, null);
        }

        return (response.StatusCode, null, await ReadCodeAsync(response));
    }

    public async Task<(HttpStatusCode Status, string? Code)> RenameAsync(Guid id, string name)
    {
        using HttpClient client = CreateClient();
        HttpResponseMessage response = await client.PutAsJsonAsync($"/api/folders/{id}/name", new { name }, JsonOptions);

        return (response.StatusCode, await ReadCodeAsync(response));
    }

    public async Task<(HttpStatusCode Status, string? Code)> DeleteAsync(Guid id)
    {
        using HttpClient client = CreateClient();
        HttpResponseMessage response = await client.DeleteAsync($"/api/folders/{id}");

        return (response.StatusCode, await ReadCodeAsync(response));
    }

    public async Task<IReadOnlyList<FolderNode>> ListAsync()
    {
        using HttpClient client = CreateClient();

        return (await client.GetFromJsonAsync<IReadOnlyList<FolderNode>>("/api/folders", JsonOptions))!;
    }

    private static async Task<string?> ReadCodeAsync(HttpResponseMessage response)
    {
        // Success responses (e.g. 204 No Content on delete) have no body. Only failures are RFC 9457
        // ProblemDetails carrying the stable code in the "code" extension.
        string body = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        using var document = JsonDocument.Parse(body);

        return document.RootElement.TryGetProperty("code", out JsonElement code) ? code.GetString() : null;
    }

    private sealed record CreatedBody(Guid Id);
}
