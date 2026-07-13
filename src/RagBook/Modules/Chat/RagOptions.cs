namespace RagBook.Modules.Chat;

/// <summary>
/// Config-driven RAG retrieval parameters (constitution §V — no magic numbers). Bound from the <c>Rag</c>
/// section. US-13 needs only <see cref="TopK"/>; the similarity threshold and grounding sentinel are
/// added by US-14/US-17.
/// </summary>
public sealed class RagOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Rag";

    /// <summary>Maximum number of passages a retrieval returns (the vector-search <c>LIMIT</c>).</summary>
    public int TopK { get; set; } = 8;

    /// <summary>
    /// Minimum cosine similarity for a passage to ground an answer (US-14). A match is kept iff its cosine
    /// similarity (<c>1 − distance</c>) is at least this — i.e. its distance is at most <c>1 − SimilarityThreshold</c>.
    /// If none qualify, the pipeline returns "insufficient grounding" without calling the model.
    /// </summary>
    public double SimilarityThreshold { get; set; } = 0.75;

    /// <summary>Maximum characters of grounding context in the prompt; the weakest passages are dropped first (US-14).</summary>
    public int MaxContextChars { get; set; } = 8000;

    /// <summary>Maximum length of a question; a longer one is rejected as <c>chat.invalid_question</c> (US-14).</summary>
    public int MaxQuestionChars { get; set; } = 2000;

    /// <summary>
    /// Interval, in seconds, between keep-alive SSE comments during a long answer stream (US-15). Prevents an
    /// intermediary (e.g. Cloud Run proxy) idle-timeout from cutting the stream.
    /// </summary>
    public int StreamHeartbeatSeconds { get; set; } = 15;
}
