using RagBook.Shared.Messaging;
using RagBook.Shared.Results;

namespace RagBook.Modules.Chat.Features.Conversations.GetConversation;

/// <summary>Loads a session's conversation with its ordered messages (US-18); a cross-session id fails as not-found.</summary>
/// <param name="Id">The conversation id.</param>
public sealed record GetConversationQuery(Guid Id) : IQuery<Result<ConversationDetail>>;
