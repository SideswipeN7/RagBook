namespace RagBook.Modules.Chat.Domain;

/// <summary>
/// Selects the recent conversational context for the prompt (US-18): the last <c>pairs</c> complete
/// <c>(user, assistant)</c> turns, in chronological order. A trailing lone user message (e.g. the current
/// in-flight question already persisted at ask start) is excluded — only closed pairs are history. Pure.
/// </summary>
public static class ConversationHistory
{
    /// <summary>Returns the last <paramref name="pairs"/> complete user→assistant turns of <paramref name="messages"/>, flattened in order.</summary>
    public static IReadOnlyList<Message> SelectRecent(IReadOnlyList<Message> messages, int pairs)
    {
        if (pairs <= 0)
        {
            return [];
        }

        var closed = new List<(Message User, Message Assistant)>();
        Message? pendingUser = null;

        foreach (Message message in messages.OrderBy(message => message.CreatedAt))
        {
            if (message.Role == MessageRole.User)
            {
                pendingUser = message;
            }
            else if (message.Role == MessageRole.Assistant && pendingUser is not null)
            {
                closed.Add((pendingUser, message));
                pendingUser = null;
            }
        }

        return closed.TakeLast(pairs).SelectMany(pair => new[] { pair.User, pair.Assistant }).ToList();
    }
}
