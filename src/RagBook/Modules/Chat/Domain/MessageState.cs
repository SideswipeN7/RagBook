namespace RagBook.Modules.Chat.Domain;

/// <summary>
/// The persisted state of an assistant message (US-18), mirroring the runtime states from US-15/17. Persisted
/// as a lowercase string matching the SSE <c>done.state</c> values plus the abort state. User messages carry none.
/// </summary>
public enum MessageState
{
    /// <summary>A produced answer (US-17 <c>done.state = answered</c>).</summary>
    Answered = 0,

    /// <summary>A grounded refusal — deterministic cut-off or prompt sentinel (US-17 <c>done.state = no_answer</c>).</summary>
    NoAnswer = 1,

    /// <summary>The stream was interrupted mid-answer by a client disconnect (US-15); the partial text is kept.</summary>
    Interrupted = 2,
}
