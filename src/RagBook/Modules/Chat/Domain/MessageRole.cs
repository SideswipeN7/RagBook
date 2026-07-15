namespace RagBook.Modules.Chat.Domain;

/// <summary>Who authored a conversation message (US-18). Persisted as a lowercase string.</summary>
public enum MessageRole
{
    /// <summary>A question typed by the user.</summary>
    User = 0,

    /// <summary>An answer produced (or refused) by the assistant.</summary>
    Assistant = 1,
}
