using RagBook.Modules.Chat.Domain;

namespace RagBook.Modules.Chat.Features.Conversations.ListConversations;

/// <summary>Handles <see cref="ListConversationsQuery"/> (US-18): the session's conversations as summaries, newest first.</summary>
public sealed class ListConversationsQueryHandler(IConversationRepository repository)
{
    /// <summary>Returns the session's conversation summaries (most-recent first).</summary>
    public async Task<IReadOnlyList<ConversationSummary>> Handle(ListConversationsQuery query, CancellationToken cancellationToken)
    {
        IReadOnlyList<Conversation> conversations = await repository.ListForSessionAsync(cancellationToken);

        return conversations.Select(ConversationSummary.From).ToList();
    }
}
