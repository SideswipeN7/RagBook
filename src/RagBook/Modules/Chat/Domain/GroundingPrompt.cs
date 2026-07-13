namespace RagBook.Modules.Chat.Domain;

/// <summary>
/// The grounding prompt contract (US-14). The system instructions constrain the model to answer ONLY from
/// the supplied numbered passages, cite claims with <c>[n]</c>, refuse with the exact <see cref="RefusalPhrase"/>
/// when the passages do not support an answer, and answer in the question's language. <see cref="RefusalPhrase"/>
/// is a fixed sentinel US-17 detects verbatim — do NOT reword it without updating US-17. Kept here as a single
/// maintained artifact, not scattered string literals.
/// </summary>
public static class GroundingPrompt
{
    /// <summary>The exact refusal sentence the model must emit when the passages do not answer the question (US-17 matches it verbatim).</summary>
    public const string RefusalPhrase = "Nie znalazłem odpowiedzi w wybranych dokumentach.";

    /// <summary>
    /// True when a completed answer <b>is</b> the refusal sentinel (US-17): trimmed ordinal equality to
    /// <see cref="RefusalPhrase"/>. The prompt requires "exactly this sentence and nothing else", so a longer
    /// answer that merely contains — or opens with — the phrase is a normal answer, not a refusal.
    /// </summary>
    public static bool IsRefusal(string answer)
    {
        return answer.Trim().Equals(RefusalPhrase, StringComparison.Ordinal);
    }

    /// <summary>The system instructions prepended to every grounded answer.</summary>
    public static readonly string SystemInstructions =
        $"""
         You are RagBook's document assistant. Answer the user's question USING ONLY the numbered source
         passages given in the user message.
         - Base every statement solely on those passages; never use outside knowledge or invent facts.
         - Mark each claim with the number(s) of the source passage(s) it comes from, in square brackets, e.g. [1] or [2][3].
         - If the passages do not contain enough information to answer, reply with EXACTLY this sentence and nothing else: {RefusalPhrase}
         - Answer in the same language as the question.
         """;
}
