using System.Text.Json;
using Microsoft.Extensions.Options;
using RagBook.API.ProblemDetails;
using RagBook.Modules.Chat;
using RagBook.Modules.Chat.Domain;
using RagBook.Modules.Chat.Errors;
using RagBook.Modules.Settings.Domain;
using RagBook.Modules.Settings.Errors;
using RagBook.Shared.Results;

namespace RagBook.API.Endpoints;

/// <summary>
/// Maps the streaming RAG ask (US-14). <c>POST /api/chat/ask</c> validates + guards + retrieves + thresholds,
/// then streams the grounded answer as <c>text/event-stream</c> (`sources` → `token`s → `done`). Pre-generation
/// failures (invalid question, missing key, not-visible scope) and a provider failure **before the first delta**
/// are normal ProblemDetails; a provider failure **mid-stream** is an SSE `error` event. The question is in the
/// body (never the URL).
/// </summary>
public static class ChatEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Maps <c>POST /api/chat/ask</c>.</summary>
    public static IEndpointRouteBuilder MapChatEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/chat/ask", async (
            AskQuestionRequest request,
            HttpContext httpContext,
            IAnthropicClientFactory clientFactory,
            IAskQuestionPipeline pipeline,
            IAnswerGenerator generator,
            IOptions<RagOptions> ragOptions,
            CancellationToken cancellationToken) =>
        {
            if (!TryBuildScope(request.Scope, out ChatScope scope))
            {
                await WriteProblemAsync(httpContext, ChatErrors.InvalidQuestion);

                return;
            }

            // Pre-generation key guard → 401 before any provider call (US-02).
            if (clientFactory.CreateForSession().IsFailure)
            {
                await WriteProblemAsync(httpContext, SettingsErrors.ApiKeyMissing);

                return;
            }

            Result<AskOutcome> prepared = await pipeline.PrepareAsync(request.Question, scope, cancellationToken);
            if (prepared.IsFailure)
            {
                await WriteProblemAsync(httpContext, prepared.Error);

                return;
            }

            AskOutcome outcome = prepared.Value;
            if (!outcome.IsAnswerable)
            {
                await StreamInsufficientAsync(httpContext, cancellationToken);

                return;
            }

            await StreamAnswerAsync(httpContext, generator, outcome.Context!, ragOptions.Value.StreamHeartbeatSeconds, cancellationToken);
        });

        return endpoints;
    }

    private static async Task StreamInsufficientAsync(HttpContext httpContext, CancellationToken cancellationToken)
    {
        StartEventStream(httpContext);
        await WriteEventAsync(httpContext, "done", new { groundsFound = false }, cancellationToken);
    }

    private static async Task StreamAnswerAsync(
        HttpContext httpContext,
        IAnswerGenerator generator,
        GroundedContext context,
        int heartbeatSeconds,
        CancellationToken cancellationToken)
    {
        await using IAsyncEnumerator<string> deltas = generator.GenerateAsync(context, cancellationToken).GetAsyncEnumerator(cancellationToken);

        // Peek the first delta BEFORE writing any response: a failure here is still a ProblemDetails (headers
        // not yet sent); a failure after this is an SSE `error` event (US-14 AC-5).
        string? firstDelta;
        try
        {
            firstDelta = await deltas.MoveNextAsync() ? deltas.Current : null;
        }
        catch (AnswerGenerationException exception)
        {
            await WriteProblemAsync(httpContext, ToError(exception.Failure));

            return;
        }

        StartEventStream(httpContext);

        // One writer for both producers (events + the keep-alive) so their writes never interleave (US-15 FR-010).
        using var writeLock = new SemaphoreSlim(1, 1);
        using var streamDone = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task heartbeat = HeartbeatAsync(httpContext, writeLock, heartbeatSeconds, streamDone.Token);

        try
        {
            IEnumerable<SourceDto> sources = context.Sources
                .Select(passage => new SourceDto(passage.Number, passage.DocumentId, passage.FileName, passage.PageNumber));
            await WriteEventGuardedAsync(httpContext, writeLock, "sources", sources, cancellationToken);

            if (firstDelta is not null)
            {
                await WriteEventGuardedAsync(httpContext, writeLock, "token", new { text = firstDelta }, cancellationToken);
            }

            while (true)
            {
                string next;
                try
                {
                    if (!await deltas.MoveNextAsync())
                    {
                        break;
                    }

                    next = deltas.Current;
                }
                catch (AnswerGenerationException exception)
                {
                    await WriteEventGuardedAsync(httpContext, writeLock, "error", new { code = ToError(exception.Failure).Code }, cancellationToken);

                    return;
                }

                await WriteEventGuardedAsync(httpContext, writeLock, "token", new { text = next }, cancellationToken);
            }

            await WriteEventGuardedAsync(httpContext, writeLock, "done", new { groundsFound = true }, cancellationToken);
        }
        finally
        {
            await streamDone.CancelAsync();
            try
            {
                await heartbeat;
            }
            catch (OperationCanceledException)
            {
                // Expected — the heartbeat is cancelled when the stream ends.
            }
        }
    }

    private static async Task HeartbeatAsync(HttpContext httpContext, SemaphoreSlim writeLock, int intervalSeconds, CancellationToken cancellationToken)
    {
        if (intervalSeconds <= 0)
        {
            return;
        }

        var interval = TimeSpan.FromSeconds(intervalSeconds);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(interval, cancellationToken);
                await writeLock.WaitAsync(cancellationToken);
                try
                {
                    await httpContext.Response.WriteAsync(": keep-alive\n\n", cancellationToken);
                    await httpContext.Response.Body.FlushAsync(cancellationToken);
                }
                finally
                {
                    writeLock.Release();
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal: the stream finished or the client disconnected.
        }
    }

    private static async Task WriteEventGuardedAsync(
        HttpContext httpContext,
        SemaphoreSlim writeLock,
        string eventName,
        object payload,
        CancellationToken cancellationToken)
    {
        await writeLock.WaitAsync(cancellationToken);
        try
        {
            await WriteEventAsync(httpContext, eventName, payload, cancellationToken);
        }
        finally
        {
            writeLock.Release();
        }
    }

    private static bool TryBuildScope(ScopeDto dto, out ChatScope scope)
    {
        switch (dto?.Type?.ToLowerInvariant())
        {
            case "all":
                scope = ChatScope.All();

                return true;
            case "folder" when dto.TargetId is Guid folderId:
                scope = ChatScope.Folder(folderId);

                return true;
            case "document" when dto.TargetId is Guid documentId:
                scope = ChatScope.Document(documentId);

                return true;
            default:
                scope = ChatScope.All();

                return false;
        }
    }

    private static Error ToError(AnswerGenerationFailure failure)
    {
        return failure switch
        {
            AnswerGenerationFailure.InvalidKey => SettingsErrors.InvalidApiKey,
            AnswerGenerationFailure.RateLimited => ChatErrors.ProviderRateLimited,
            _ => ChatErrors.ProviderUnavailable,
        };
    }

    private static void StartEventStream(HttpContext httpContext)
    {
        httpContext.Response.Headers.ContentType = "text/event-stream";
        httpContext.Response.Headers.CacheControl = "no-cache";
    }

    private static async Task WriteEventAsync(HttpContext httpContext, string eventName, object payload, CancellationToken cancellationToken)
    {
        string json = JsonSerializer.Serialize(payload, JsonOptions);
        await httpContext.Response.WriteAsync($"event: {eventName}\ndata: {json}\n\n", cancellationToken);
        await httpContext.Response.Body.FlushAsync(cancellationToken);
    }

    private static Task WriteProblemAsync(HttpContext httpContext, Error error)
    {
        return ProblemResults.Problem(error).ExecuteAsync(httpContext);
    }
}
