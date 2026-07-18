using Microsoft.Extensions.Options;
using RagBook.Modules.Demo;
using RagBook.Modules.Demo.Domain;
using RagBook.Modules.Settings.Domain;

namespace RagBook.API.Endpoints;

/// <summary>
/// Maps the demo status read (US-03). <c>GET /api/demo/status</c> reports how many demo questions the current
/// session has used and whether demo generation is configured, so the UI can show "X / N pytań demo" and the BYOK
/// nudge. The application key itself is never exposed — only the boolean <c>available</c>.
/// </summary>
public static class DemoEndpoints
{
    /// <summary>Maps <c>GET /api/demo/status</c>.</summary>
    public static IEndpointRouteBuilder MapDemoEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/demo/status", (
            IDemoQuestionCounter counter,
            IAnthropicClientFactory clientFactory,
            IOptions<DemoOptions> options) =>
        {
            int max = options.Value.MaxQuestionsPerSession;
            int asked = counter.Asked();
            bool available = clientFactory.CreateForDemo().IsSuccess;

            return Results.Ok(new DemoStatusResponse(asked, max, Math.Max(0, max - asked), available));
        });

        return endpoints;
    }
}

/// <summary>Demo usage for the current session (US-03).</summary>
/// <param name="Asked">Demo questions already asked this session.</param>
/// <param name="Max">The per-session demo question limit.</param>
/// <param name="Remaining">Demo questions left before the limit.</param>
/// <param name="Available">Whether demo generation is configured (an application key is present).</param>
public sealed record DemoStatusResponse(int Asked, int Max, int Remaining, bool Available);
