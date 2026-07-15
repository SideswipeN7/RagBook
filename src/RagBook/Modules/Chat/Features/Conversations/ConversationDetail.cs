namespace RagBook.Modules.Chat.Features.Conversations;

/// <summary>A message as loaded for history replay (US-18): role/state as wire strings, sources as raw JSON.</summary>
/// <param name="Id">The message id.</param>
/// <param name="Role">The author — <c>user</c> | <c>assistant</c>.</param>
/// <param name="Content">The question or answer text.</param>
/// <param name="State">Assistant state — <c>answered</c> | <c>no_answer</c> | <c>interrupted</c>; <c>null</c> for a user message.</param>
/// <param name="SourcesJson">The assistant citations as a <c>SourceDto[]</c> JSON document, or <c>null</c>.</param>
/// <param name="CreatedAt">When the message was created (ordering key).</param>
public sealed record MessageView(Guid Id, string Role, string Content, string? State, string? SourcesJson, DateTimeOffset CreatedAt);

/// <summary>A conversation with its ordered messages (US-18), for reopening it.</summary>
/// <param name="Summary">The conversation summary.</param>
/// <param name="Messages">The messages in chronological order.</param>
public sealed record ConversationDetail(ConversationSummary Summary, IReadOnlyList<MessageView> Messages);
