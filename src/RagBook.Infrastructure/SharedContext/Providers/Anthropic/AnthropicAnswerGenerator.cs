using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using RagBook.Modules.Chat.Domain;
using RagBook.Modules.Settings.Domain;
using RagBook.Shared.Results;

namespace RagBook.Infrastructure.SharedContext.Providers.Anthropic;

/// <summary>
/// Streaming <see cref="IAnswerGenerator"/> over Anthropic's <c>POST /v1/messages</c> (<c>stream:true</c>),
/// keyed by the session's BYOK key (US-02 <see cref="IAnthropicClientFactory"/>). Parses the response SSE,
/// yielding <c>content_block_delta</c> text as it arrives; a non-2xx status, an SSE <c>error</c> event, or a
/// transport failure becomes an <see cref="AnswerGenerationException"/> (401/403→InvalidKey, 429→RateLimited,
/// else→Unavailable). The response is read with <see cref="HttpCompletionOption.ResponseHeadersRead"/> and the
/// client has NO total-request-timeout/retry (that would truncate or re-issue a live stream — C1); cancellation
/// flows via the request token (client disconnect).
/// </summary>
public sealed class AnthropicAnswerGenerator(
    IHttpClientFactory httpClientFactory,
    IAnthropicClientFactory clientFactory,
    IOptions<AnthropicOptions> options)
    : IAnswerGenerator
{
    /// <summary>The named client configured without a stream-truncating total timeout in <c>AddInfrastructure</c>.</summary>
    public const string HttpClientName = "anthropic-generation";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <inheritdoc />
    public async IAsyncEnumerable<string> GenerateAsync(
        GroundedContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Result<AnthropicClientHandle> handle = clientFactory.CreateForSession();
        if (handle.IsFailure)
        {
            throw new AnswerGenerationException(AnswerGenerationFailure.InvalidKey);
        }

        using HttpResponseMessage response = await SendAsync(context, handle.Value.ApiKey, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new AnswerGenerationException(MapStatus(response.StatusCode));
        }

        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        while (true)
        {
            string? line;
            try
            {
                line = await reader.ReadLineAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                throw new AnswerGenerationException(AnswerGenerationFailure.Unavailable);
            }

            if (line is null)
            {
                break;
            }

            DeltaLine parsed = ParseLine(line);
            if (parsed.Failure is AnswerGenerationFailure failure)
            {
                throw new AnswerGenerationException(failure);
            }

            if (parsed.Stop)
            {
                break;
            }

            if (parsed.Text is string text)
            {
                yield return text;
            }
        }
    }

    private async Task<HttpResponseMessage> SendAsync(GroundedContext context, string apiKey, CancellationToken cancellationToken)
    {
        AnthropicOptions settings = options.Value;
        HttpClient client = httpClientFactory.CreateClient(HttpClientName);

        var payload = new
        {
            model = settings.GenerationModel,
            max_tokens = settings.MaxOutputTokens,
            stream = true,
            system = context.SystemPrompt,
            messages = new[] { new { role = "user", content = context.UserPrompt } },
        };

        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/messages")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("x-api-key", apiKey);
        request.Headers.TryAddWithoutValidation("anthropic-version", settings.AnthropicVersion);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        try
        {
            return await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            throw new AnswerGenerationException(AnswerGenerationFailure.Unavailable);
        }
    }

    private static AnswerGenerationFailure MapStatus(HttpStatusCode status)
    {
        return status switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => AnswerGenerationFailure.InvalidKey,
            HttpStatusCode.TooManyRequests => AnswerGenerationFailure.RateLimited,
            _ => AnswerGenerationFailure.Unavailable,
        };
    }

    private readonly record struct DeltaLine(string? Text, bool Stop, AnswerGenerationFailure? Failure);

    private static DeltaLine ParseLine(string line)
    {
        // Only the SSE data payloads carry the typed JSON; event:/blank lines are ignored.
        if (!line.StartsWith("data:", StringComparison.Ordinal))
        {
            return default;
        }

        string json = line["data:".Length..].Trim();
        if (json.Length == 0)
        {
            return default;
        }

        JsonElement root;
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            root = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return default;
        }

        string? type = root.TryGetProperty("type", out JsonElement typeElement) ? typeElement.GetString() : null;

        return type switch
        {
            "content_block_delta" => new DeltaLine(ExtractDeltaText(root), Stop: false, Failure: null),
            "message_stop" => new DeltaLine(Text: null, Stop: true, Failure: null),
            "error" => new DeltaLine(Text: null, Stop: false, Failure: MapErrorEvent(root)),
            _ => default,
        };
    }

    private static string? ExtractDeltaText(JsonElement root)
    {
        return root.TryGetProperty("delta", out JsonElement delta)
               && delta.TryGetProperty("text", out JsonElement text)
            ? text.GetString()
            : null;
    }

    private static AnswerGenerationFailure MapErrorEvent(JsonElement root)
    {
        string? errorType = root.TryGetProperty("error", out JsonElement error)
                            && error.TryGetProperty("type", out JsonElement typeElement)
            ? typeElement.GetString()
            : null;

        return errorType switch
        {
            "authentication_error" or "permission_error" => AnswerGenerationFailure.InvalidKey,
            "rate_limit_error" => AnswerGenerationFailure.RateLimited,
            _ => AnswerGenerationFailure.Unavailable,
        };
    }
}
