using RagBook.Shared.Messaging;

namespace RagBook.Modules.Chat.Features.Conversations.DeleteConversation;

/// <summary>Hard-deletes a session-owned conversation and its messages (US-18; the messages cascade at the database).</summary>
/// <param name="Id">The conversation to delete.</param>
public sealed record DeleteConversationCommand(Guid Id) : ICommand;
