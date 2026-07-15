using RagBook.Modules.Documents.Domain;

namespace RagBook.Modules.Chat.Domain;

/// <summary>
/// Assembles the grounding context (US-14) from a question and the retrieved passages: numbers them
/// <c>[1..K]</c> most-relevant first, formats each with its file + page, and trims the assembled context to
/// the configured budget by dropping the weakest whole passages first.
/// </summary>
public interface IPromptBuilder
{
    /// <summary>
    /// Builds the grounded context; <paramref name="passages"/> are ordered most-relevant first (US-13), and
    /// <paramref name="history"/> is the recent conversation turns prepended as context (US-18; empty = single-turn).
    /// </summary>
    GroundedContext Build(string question, IReadOnlyList<RetrievedChunk> passages, IReadOnlyList<Message> history);
}
