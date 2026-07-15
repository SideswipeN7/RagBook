namespace RagBook.Modules.Chat;

/// <summary>
/// Config-driven conversation parameters (constitution §VII — no magic numbers). Bound from the <c>Chat</c>
/// section (US-18). Retrieval/threshold parameters stay in <see cref="RagOptions"/>.
/// </summary>
public sealed class ChatOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Chat";

    /// <summary>
    /// How many recent <c>(user, assistant)</c> message pairs are added to the prompt as conversational
    /// context (US-18). Older turns stay in the UI only. Bounds prompt cost/size (AC-4).
    /// </summary>
    public int HistoryPairs { get; set; } = 6;

    /// <summary>Maximum length of a conversation title — the first question is truncated to this (US-18, no LLM titling).</summary>
    public int TitleMaxChars { get; set; } = 60;
}
