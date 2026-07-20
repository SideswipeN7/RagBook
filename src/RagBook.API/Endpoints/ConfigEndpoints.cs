using Microsoft.Extensions.Options;
using RagBook.Modules.Chat;

namespace RagBook.API.Endpoints;

/// <summary>
/// Exposes read-only client bootstrap flags (US-22). <c>GET /api/config</c> tells the SPA whether the server can
/// generate a <b>regular</b> (non-demo) answer without a session key — true only in keyless CLI mode, which routes
/// any scope through the local CLI. A server application key is deliberately <b>not</b> counted: it pays only for the
/// demo scope (which the composer already unlocks on its own), so counting it would unlock the composer for scopes
/// the backend still 401s. No secrets are returned.
/// </summary>
public static class ConfigEndpoints
{
    /// <summary>Maps <c>GET /api/config</c>.</summary>
    public static IEndpointRouteBuilder MapConfigEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/config", (IOptions<ClaudeCliOptions> cliOptions) =>
        {
            // Mirrors the ChatEndpoints key-guard relaxation, which keys off CLI mode only — so the composer unlock
            // and the backend guard never disagree.
            bool keylessGeneration = cliOptions.Value.Enabled;

            return Results.Ok(new AppConfigResponse(keylessGeneration));
        });

        return endpoints;
    }

    /// <summary>Client bootstrap flags. <paramref name="KeylessGeneration"/>: the server can answer without a session key.</summary>
    private sealed record AppConfigResponse(bool KeylessGeneration);
}
