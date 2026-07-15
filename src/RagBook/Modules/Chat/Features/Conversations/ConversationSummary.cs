using RagBook.Modules.Chat.Domain;

namespace RagBook.Modules.Chat.Features.Conversations;

/// <summary>A conversation's list/summary projection (US-18): identity, title, current scope, creation time.</summary>
/// <param name="Id">The conversation id.</param>
/// <param name="Title">The title (first question, truncated); empty until the first ask.</param>
/// <param name="ScopeType">The current scope kind — <c>all</c> | <c>folder</c> | <c>document</c>.</param>
/// <param name="ScopeTargetId">The folder/document id for a scoped conversation; <c>null</c> for <c>all</c>.</param>
/// <param name="CreatedAt">When the conversation was created.</param>
public sealed record ConversationSummary(Guid Id, string Title, string ScopeType, Guid? ScopeTargetId, DateTimeOffset CreatedAt)
{
    /// <summary>Projects a <see cref="Conversation"/> to its summary, with the scope kind as a lowercase string.</summary>
    public static ConversationSummary From(Conversation conversation)
    {
        return new ConversationSummary(
            conversation.Id,
            conversation.Title,
            conversation.ScopeType.ToString().ToLowerInvariant(),
            conversation.ScopeTargetId,
            conversation.CreatedAt);
    }
}
