namespace RagBook.Modules.Chat.Domain;

/// <summary>The distinguishable ways answer generation can fail (US-14 AC-5).</summary>
public enum AnswerGenerationFailure
{
    /// <summary>The provider rejected the key → <c>settings.invalid_api_key</c>.</summary>
    InvalidKey = 0,

    /// <summary>The provider throttled the request → <c>chat.provider_rate_limited</c>.</summary>
    RateLimited = 1,

    /// <summary>The provider is unavailable / timed out / server error → <c>chat.provider_unavailable</c>.</summary>
    Unavailable = 2,
}

/// <summary>
/// Thrown by <see cref="IAnswerGenerator"/> when generation fails. The endpoint maps <see cref="Failure"/>
/// to a distinct code — a failure before the first delta becomes a ProblemDetails, one mid-stream an SSE
/// <c>error</c> event (US-14 AC-5).
/// </summary>
public sealed class AnswerGenerationException(AnswerGenerationFailure failure)
    : Exception($"Answer generation failed: {failure}.")
{
    /// <summary>The failure kind.</summary>
    public AnswerGenerationFailure Failure { get; } = failure;
}
