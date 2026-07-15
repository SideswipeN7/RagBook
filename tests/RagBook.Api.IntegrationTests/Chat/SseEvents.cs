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

    /// <summary>Creates a conversation (US-18) and returns its id.</summary>
    public static async Task<Guid> CreateConversationAsync(HttpClient client, string scopeType = "all", Guid? targetId = null)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync("/api/conversations", new { scope = new { type = scopeType, targetId } });
        response.EnsureSuccessStatusCode();
        ConversationSummaryResponse? summary = await response.Content.ReadFromJsonAsync<ConversationSummaryResponse>();

        return summary!.Id;
    }

    /// <summary>Posts an ask in a fresh conversation (US-18) and returns the SSE (or ProblemDetails) response.</summary>
    public static async Task<HttpResponseMessage> AskAsync(HttpClient client, string question, string scopeType, Guid? targetId = null)
    {
        Guid conversationId = await CreateConversationAsync(client);

        return await AskInAsync(client, conversationId, question, scopeType, targetId);
    }

    /// <summary>Posts an ask in an explicit conversation (US-18) — for multi-turn/persistence tests.</summary>
    public static async Task<HttpResponseMessage> AskInAsync(HttpClient client, Guid conversationId, string question, string scopeType, Guid? targetId = null)
    {
        return await client.PostAsJsonAsync("/api/chat/ask", new
        {
            conversationId,
            question,
            scope = new { type = scopeType, targetId },
        });
    }

    private sealed record ConversationSummaryResponse(Guid Id, string Title, string ScopeType, Guid? ScopeTargetId, DateTimeOffset CreatedAt);

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
