using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace RagBook.Api.IntegrationTests.Settings;

/// <summary>Shared helpers for the Settings endpoint tests: a session-bound client and a ProblemDetails code reader.</summary>
internal static class SettingsTestClient
{
    /// <summary>A syntactically valid Anthropic key used across the tests.</summary>
    public const string ValidKey = "sk-ant-api03-abcdefghijklmnopqrstuvB7fA";

    /// <summary>Builds an HTTPS client that carries the given session's cookie (matching the seeded session).</summary>
    public static HttpClient CreateClient(WebApplicationFactory<Program> factory, Guid sessionId)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = false,
        });
        client.DefaultRequestHeaders.Add("Cookie", $"ragbook_session={sessionId}");

        return client;
    }

    /// <summary>Reads the stable <c>code</c> from an RFC 9457 ProblemDetails body.</summary>
    public static async Task<string?> ReadProblemCodeAsync(HttpResponseMessage response)
    {
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return document.RootElement.TryGetProperty("code", out JsonElement code) ? code.GetString() : null;
    }

    /// <summary>Posts a candidate key to the save endpoint.</summary>
    public static Task<HttpResponseMessage> PostKeyAsync(HttpClient client, string apiKey)
    {
        return client.PostAsJsonAsync("/api/settings/api-key", new { apiKey });
    }
}
