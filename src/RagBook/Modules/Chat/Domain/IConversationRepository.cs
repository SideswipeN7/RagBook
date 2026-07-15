namespace RagBook.Modules.Chat.Domain;

/// <summary>
/// Persistence seam for <see cref="Conversation"/> and its <see cref="Message"/>s (US-18). Every read is
/// constrained to the current session by the EF Core global query filter behind the implementation
/// (constitution §III) — a cross-session id reads as absent (→ 404). The assistant-message write happens in an
/// outbox handler that has first initialized the session context from the event.
/// </summary>
public interface IConversationRepository
{
    /// <summary>Persists a new conversation; the owning session is stamped centrally on save.</summary>
    Task AddAsync(Conversation conversation, CancellationToken cancellationToken);

    /// <summary>Returns the tracked conversation by id, or <c>null</c> when absent or owned by another session.</summary>
    Task<Conversation?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Returns the current session's conversations, most-recent first.</summary>
    Task<IReadOnlyList<Conversation>> ListForSessionAsync(CancellationToken cancellationToken);

    /// <summary>Returns a conversation's messages ordered chronologically (empty if absent/other session).</summary>
    Task<IReadOnlyList<Message>> ListMessagesAsync(Guid conversationId, CancellationToken cancellationToken);

    /// <summary>Persists a new message.</summary>
    Task AddMessageAsync(Message message, CancellationToken cancellationToken);

    /// <summary>Deletes the conversation (its messages cascade at the database).</summary>
    Task RemoveAsync(Conversation conversation, CancellationToken cancellationToken);

    /// <summary>Flushes pending changes (e.g. title/scope updates on a tracked conversation).</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
