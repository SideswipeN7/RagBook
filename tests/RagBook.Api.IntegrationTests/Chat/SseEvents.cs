using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace RagBook.Api.IntegrationTests.Chat;

/// <summary>Helpers for the US-14 ask endpoint: a session-bound client, a POST, and an SSE parser.</summary>
internal static class SseEvents
{
    /// <summary>One parsed SSE event.</summary>
    public sealed record Event(string Name, string Data);

    /// <summary>Builds an HTTPS client carrying the given session's cookie.</summary>
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

    /// <summary>Posts an ask and parses the SSE (or ProblemDetails) response.</summary>
    public static async Task<HttpResponseMessage> AskAsync(HttpClient client, string question, string scopeType, Guid? targetId = null)
    {
        return await client.PostAsJsonAsync("/api/chat/ask", new
        {
            question,
            scope = new { type = scopeType, targetId },
        });
    }

    /// <summary>Parses a `text/event-stream` body into its ordered events.</summary>
    public static async Task<IReadOnlyList<Event>> ReadAsync(HttpResponseMessage response)
    {
        string body = await response.Content.ReadAsStringAsync();
        var events = new List<Event>();

        foreach (string block in body.Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
        {
            string? name = null;
            string? data = null;
            foreach (string line in block.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.StartsWith("event:", StringComparison.Ordinal))
                {
                    name = line["event:".Length..].Trim();
                }
                else if (line.StartsWith("data:", StringComparison.Ordinal))
                {
                    data = line["data:".Length..].Trim();
                }
            }

            if (name is not null)
            {
                events.Add(new Event(name, data ?? string.Empty));
            }
        }

        return events;
    }
}
