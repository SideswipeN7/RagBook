using RagBook.Modules.Chat.Domain;
using RagBook.Shared.Messaging;

namespace RagBook.Modules.Chat.Features.Conversations.CreateConversation;

/// <summary>Creates an empty, session-owned conversation under an initial scope (US-18). Defaults to <c>All</c>.</summary>
/// <param name="ScopeType">The initial scope kind.</param>
/// <param name="ScopeTargetId">The folder/document id for a scoped conversation; <c>null</c> for <c>all</c>.</param>
public sealed record CreateConversationCommand(ChatScopeType ScopeType, Guid? ScopeTargetId) : ICommand<ConversationSummary>;
