using RagBook.Modules.Chat.Domain;
using RagBook.Modules.Chat.Errors;
using RagBook.Shared.Results;

namespace RagBook.Modules.Chat.Features.Conversations.GetConversation;

/// <summary>
/// Handles <see cref="GetConversationQuery"/> (US-18): returns the session's conversation + its ordered messages,
/// or <see cref="ChatErrors.ConversationNotFound"/> when absent or owned by another session (→ 404). Roles/states
/// are projected to their wire strings; sources stay as the stored JSON for the endpoint to pass through.
/// </summary>
public sealed class GetConversationQueryHandler(IConversationRepository repository)
{
    /// <summary>Loads the conversation detail or a not-found failure.</summary>
    public async Task<Result<ConversationDetail>> Handle(GetConversationQuery query, CancellationToken cancellationToken)
    {
        Conversation? conversation = await repository.GetByIdAsync(query.Id, cancellationToken);
        if (conversation is null)
        {
            return Result.Failure<ConversationDetail>(ChatErrors.ConversationNotFound);
        }

        IReadOnlyList<Message> messages = await repository.ListMessagesAsync(conversation.Id, cancellationToken);
        var views = messages
            .Select(message => new MessageView(
                message.Id,
                message.Role.ToString().ToLowerInvariant(),
                message.Content,
                ToWireState(message.State),
                message.SourcesJson,
                message.CreatedAt))
            .ToList();

        return new ConversationDetail(ConversationSummary.From(conversation), views);
    }

    private static string? ToWireState(MessageState? state)
    {
        return state switch
        {
            MessageState.Answered => "answered",
            MessageState.NoAnswer => "no_answer",
            MessageState.Interrupted => "interrupted",
            _ => null,
        };
    }
}
