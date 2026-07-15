using RagBook.Modules.Chat.Domain;
using RagBook.Shared.Sessions;

namespace RagBook.Modules.Chat.Features.AskQuestion;

/// <summary>
/// Persists the assistant message for a completed turn (US-18). Runs in the durable outbox, **outside** the
/// request's session, so it first initializes the session context from the event — otherwise the central
/// session stamping + global query filter would use the wrong (background) session.
/// </summary>
public sealed class ChatTurnCompletedHandler(IConversationRepository repository, ISessionInitializer sessionInitializer)
{
    /// <summary>Maps the wire state and persists the assistant message under the event's session.</summary>
    public async Task Handle(ChatTurnCompleted message, CancellationToken cancellationToken)
    {
        sessionInitializer.Initialize(message.UserSessionId);

        MessageState state = message.State switch
        {
            "no_answer" => MessageState.NoAnswer,
            "interrupted" => MessageState.Interrupted,
            _ => MessageState.Answered,
        };

        await repository.AddMessageAsync(
            Message.Assistant(message.ConversationId, message.Answer, state, message.SourcesJson),
            cancellationToken);
    }
}
