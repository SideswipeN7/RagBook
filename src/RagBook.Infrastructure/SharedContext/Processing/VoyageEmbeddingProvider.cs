using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using RagBook.Modules.Documents.Domain;
using RagBook.Modules.Documents.Errors;
using RagBook.Modules.Documents.Processing;

namespace RagBook.Infrastructure.SharedContext.Processing;

/// <summary>
/// Real <see cref="IEmbeddingProvider"/> over the Voyage AI embeddings API (US-06), selected when a
/// provider key is configured. Transient failures (network/timeout/5xx) surface as
/// <see cref="EmbeddingProviderException"/> so the handler's bounded retry applies; a terminal failure
/// then marks the document failed. The application key comes from configuration/Secret Manager, never DB.
/// </summary>
public sealed class VoyageEmbeddingProvider : IEmbeddingProvider
{
    private const string EmbeddingsEndpoint = "https://api.voyageai.com/v1/embeddings";

    private readonly HttpClient _httpClient;
    private readonly EmbeddingOptions _options;

    public VoyageEmbeddingProvider(HttpClient httpClient, IOptions<EmbeddingOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _httpClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _options.ApiKey);
    }

    /// <inheritdoc />
    public int Dimension => _options.Dimension;

    /// <inheritdoc />
    public async Task<IReadOnlyList<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken)
    {
        try
        {
            using HttpResponseMessage response = await _httpClient.PostAsJsonAsync(
                EmbeddingsEndpoint,
                new VoyageRequest(texts, _options.Model),
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw new EmbeddingProviderException($"Voyage returned {(int)response.StatusCode}.");
            }

            VoyageResponse? body = await response.Content.ReadFromJsonAsync<VoyageResponse>(cancellationToken);
            if (body is null)
            {
                throw new EmbeddingProviderException("Voyage returned an empty body.");
            }

            return body.Data.Select(item => item.Embedding).ToList();
        }
        catch (HttpRequestException exception)
        {
            throw new EmbeddingProviderException("Voyage request failed.", exception);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new EmbeddingProviderException("Voyage request timed out.", exception);
        }
    }

    private sealed record VoyageRequest(IReadOnlyList<string> Input, string Model);

    private sealed record VoyageResponse(List<VoyageData> Data);

    private sealed record VoyageData(float[] Embedding);
}
