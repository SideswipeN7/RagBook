using RagBook.Modules.Chat.Domain;

namespace RagBook.Modules.Chat.Features.Conversations.CreateConversation;

/// <summary>
/// Handles <see cref="CreateConversationCommand"/> (US-18): starts an empty conversation under the requested
/// scope (invalid/absent target falls back to <c>All</c>) and persists it. The owning session is stamped
/// centrally on save.
/// </summary>
public sealed class CreateConversationCommandHandler(IConversationRepository repository)
{
    /// <summary>Creates and persists the conversation, returning its summary.</summary>
    public async Task<ConversationSummary> Handle(CreateConversationCommand command, CancellationToken cancellationToken)
    {
        ChatScope scope = command.ScopeType switch
        {
            ChatScopeType.Folder when command.ScopeTargetId is Guid folderId => ChatScope.Folder(folderId),
            ChatScopeType.Document when command.ScopeTargetId is Guid documentId => ChatScope.Document(documentId),
            _ => ChatScope.All(),
        };

        Conversation conversation = Conversation.Start(scope);
        await repository.AddAsync(conversation, cancellationToken);

        return ConversationSummary.From(conversation);
    }
}
