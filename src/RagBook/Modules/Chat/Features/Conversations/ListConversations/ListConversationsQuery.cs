using RagBook.Shared.Messaging;

namespace RagBook.Modules.Chat.Features.Conversations.ListConversations;

/// <summary>Lists the current session's conversations, most-recent first (US-18).</summary>
public sealed record ListConversationsQuery : IQuery<IReadOnlyList<ConversationSummary>>;
