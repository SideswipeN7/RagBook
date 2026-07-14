using RagBook.Shared.Auditing;
using RagBook.Shared.Sessions;

namespace RagBook.Modules.Chat.Domain;

/// <summary>
/// One turn in a <see cref="Conversation"/> (US-18): a user question or an assistant answer. Assistant messages
/// carry a <see cref="State"/> (answered / no-answer / interrupted) and their citations as <see cref="SourcesJson"/>
/// (the US-16 <c>SourceDto[]</c> shape), so a citation survives its document's deletion (US-16 AC-4). Construction
/// goes through the factories. <see cref="UserSessionId"/> is stamped centrally on insert — except in the outbox
/// handler that persists the assistant message outside the request session, where it is set from the event.
/// </summary>
public sealed class Message : ISessionOwned, IAuditable
{
    private Message(Guid id, Guid conversationId, MessageRole role, string content, MessageState? state, string? sourcesJson)
    {
        Id = id;
        ConversationId = conversationId;
        Role = role;
        Content = content;
        State = state;
        SourcesJson = sourcesJson;
    }

    // Required by EF Core for materialization.
    private Message()
    {
        Content = string.Empty;
    }

    /// <summary>Identity (GUID v4).</summary>
    public Guid Id { get; private set; }

    /// <summary>The owning conversation.</summary>
    public Guid ConversationId { get; private set; }

    /// <summary>Who authored the message.</summary>
    public MessageRole Role { get; private set; }

    /// <summary>The question text, or the assistant answer (partial when <see cref="MessageState.Interrupted"/>).</summary>
    public string Content { get; private set; }

    /// <summary>Assistant-message state; <c>null</c> for a user message.</summary>
    public MessageState? State { get; private set; }

    /// <summary>The assistant citations as a <c>SourceDto[]</c> JSON document; <c>null</c> for user/no-source messages.</summary>
    public string? SourcesJson { get; private set; }

    /// <inheritdoc />
    public Guid UserSessionId { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset CreatedAt { get; set; }

    /// <inheritdoc />
    public string CreatedBy { get; set; } = string.Empty;

    /// <inheritdoc />
    public DateTimeOffset? ModifiedAt { get; set; }

    /// <inheritdoc />
    public string? ModifiedBy { get; set; }

    /// <summary>A user question in <paramref name="conversationId"/>.</summary>
    public static Message User(Guid conversationId, string content)
    {
        return new Message(Guid.NewGuid(), conversationId, MessageRole.User, content, state: null, sourcesJson: null);
    }

    /// <summary>An assistant answer/refusal with its final <paramref name="state"/> and optional <paramref name="sourcesJson"/>.</summary>
    public static Message Assistant(Guid conversationId, string content, MessageState state, string? sourcesJson)
    {
        return new Message(Guid.NewGuid(), conversationId, MessageRole.Assistant, content, state, sourcesJson);
    }

    /// <summary>Stamps the owning session explicitly — for the outbox handler that persists outside the request session (US-18).</summary>
    public void OwnBySession(Guid userSessionId)
    {
        UserSessionId = userSessionId;
    }
}
