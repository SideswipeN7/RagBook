using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Options;
using RagBook.Infrastructure.SharedContext.Providers.Anthropic;
using RagBook.Modules.Settings.Domain;

namespace RagBook.Infrastructure.SharedContext.Settings;

/// <summary>
/// <see cref="IApiKeyValidator"/> backed by a non-generative call to Anthropic's <c>GET /v1/models</c>
/// (auth-checked, zero token cost). Status codes map cleanly onto the three-way outcome: <c>200</c> →
/// <see cref="ApiKeyValidationResult.Valid"/>; <c>401/403</c> → <see cref="ApiKeyValidationResult.Rejected"/>;
/// throttling / server / network / timeout → <see cref="ApiKeyValidationResult.Unavailable"/> (transient).
/// The call rides a named <see cref="HttpClient"/> with a standard resilience handler (constitution §V).
/// </summary>
public sealed class AnthropicApiKeyValidator(
    IHttpClientFactory httpClientFactory,
    IOptions<AnthropicOptions> options)
    : IApiKeyValidator
{
    /// <summary>The named client configured with resilience in <c>AddInfrastructure</c>.</summary>
    public const string HttpClientName = "anthropic-validation";

    /// <inheritdoc />
    public async Task<ApiKeyValidationResult> ValidateAsync(string apiKey, CancellationToken cancellationToken)
    {
        AnthropicOptions settings = options.Value;
        HttpClient client = httpClientFactory.CreateClient(HttpClientName);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/v1/models");
        request.Headers.TryAddWithoutValidation("x-api-key", apiKey);
        request.Headers.TryAddWithoutValidation("anthropic-version", settings.AnthropicVersion);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        try
        {
            using HttpResponseMessage response = await client.SendAsync(request, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return ApiKeyValidationResult.Valid;
            }

            return response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                ? ApiKeyValidationResult.Rejected
                : ApiKeyValidationResult.Unavailable;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Any non-definitive failure (network error, per-attempt timeout, resilience-pipeline
            // TimeoutRejectedException / BrokenCircuitException, throttling) is transient, not a
            // rejection — a liveness check that cannot conclude means "could not verify".
            return ApiKeyValidationResult.Unavailable;
        }
    }
}
