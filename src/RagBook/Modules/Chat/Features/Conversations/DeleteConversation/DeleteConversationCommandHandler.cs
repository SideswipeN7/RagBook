using RagBook.Modules.Chat.Domain;
using RagBook.Modules.Chat.Errors;
using RagBook.Shared.Results;

namespace RagBook.Modules.Chat.Features.Conversations.DeleteConversation;

/// <summary>
/// Handles <see cref="DeleteConversationCommand"/> (US-18): hard-deletes the conversation (its messages cascade
/// at the database). A conversation owned by another session is invisible to the repository, so it reads as
/// <see cref="ChatErrors.ConversationNotFound"/> (→ 404), never disclosing existence.
/// </summary>
public sealed class DeleteConversationCommandHandler(IConversationRepository repository)
{
    /// <summary>Deletes the conversation, or returns a not-found failure.</summary>
    public async Task<Result> Handle(DeleteConversationCommand command, CancellationToken cancellationToken)
    {
        Conversation? conversation = await repository.GetByIdAsync(command.Id, cancellationToken);
        if (conversation is null)
        {
            return Result.Failure(ChatErrors.ConversationNotFound);
        }

        await repository.RemoveAsync(conversation, cancellationToken);

        return Result.Success();
    }
}
