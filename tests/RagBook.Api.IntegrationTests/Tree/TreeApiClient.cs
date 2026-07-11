using System.Net.Http.Json;
using System.Text.Json;
using RagBook.Modules.Tree.Features.GetTree;

namespace RagBook.Api.IntegrationTests.Tree;

/// <summary>Reads <c>GET /api/tree</c> for a chosen session.</summary>
internal sealed class TreeApiClient(RagBookApiFactory factory, Guid sessionId)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<TreeResponse> GetAsync()
    {
        var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = false,
        });
        client.DefaultRequestHeaders.Add("Cookie", $"ragbook_session={sessionId}");

        return (await client.GetFromJsonAsync<TreeResponse>("/api/tree", JsonOptions))!;
    }
}
