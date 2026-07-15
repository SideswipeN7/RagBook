using RagBook.Shared.Auditing;
using RagBook.Shared.Sessions;

namespace RagBook.Modules.Chat.Domain;

/// <summary>
/// A session-owned chat thread (US-18): an ordered set of <see cref="Message"/>s under a current <see cref="Scope"/>
/// and a <see cref="Title"/> derived from the first question (≤ configured length, no LLM). Scope is changeable
/// per ask — <see cref="UpdateScope"/> records the latest one so reopening restores the selector. Construction
/// goes through <see cref="Start"/>; <see cref="UserSessionId"/> is stamped centrally on insert.
/// </summary>
public sealed class Conversation : ISessionOwned, IAuditable
{
    private Conversation(Guid id, ChatScopeType scopeType, Guid? scopeTargetId, string title)
    {
        Id = id;
        ScopeType = scopeType;
        ScopeTargetId = scopeTargetId;
        Title = title;
    }

    // Required by EF Core for materialization.
    private Conversation()
    {
        Title = string.Empty;
    }

    /// <summary>Identity (GUID v4).</summary>
    public Guid Id { get; private set; }

    /// <summary>The current scope boundary (US-13); updated to each ask's scope.</summary>
    public ChatScopeType ScopeType { get; private set; }

    /// <summary>The folder/document id for <see cref="ChatScopeType.Folder"/>/<see cref="ChatScopeType.Document"/>; <c>null</c> for <see cref="ChatScopeType.All"/>.</summary>
    public Guid? ScopeTargetId { get; private set; }

    /// <summary>The current scope as a <see cref="ChatScope"/> value.</summary>
    public ChatScope Scope => ScopeType switch
    {
        ChatScopeType.Folder => ChatScope.Folder(ScopeTargetId!.Value),
        ChatScopeType.Document => ChatScope.Document(ScopeTargetId!.Value),
        _ => ChatScope.All(),
    };

    /// <summary>Display title — the first question truncated to the configured length; empty until the first ask.</summary>
    public string Title { get; private set; }

    /// <inheritdoc />
    public Guid UserSessionId { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset CreatedAt { get; set; }

    /// <inheritdoc />
    public string CreatedBy { get; set; } = string.Empty;

    /// <inheritdoc />
    public DateTimeOffset? ModifiedAt { get; set; }

    /// <inheritdoc />
    public string? ModifiedBy { get; set; }

    /// <summary>Starts an empty conversation with the given initial <paramref name="scope"/> and no title yet.</summary>
    public static Conversation Start(ChatScope scope)
    {
        return new Conversation(Guid.NewGuid(), scope.Type, scope.TargetId, title: string.Empty);
    }

    /// <summary>
    /// Sets the title from the first question — trimmed and truncated to <paramref name="maxChars"/> — but only
    /// while the title is still empty, so later turns never rewrite it (US-18 FR-006).
    /// </summary>
    public void SetTitleFromFirstQuestion(string question, int maxChars)
    {
        if (Title.Length > 0)
        {
            return;
        }

        string trimmed = question.Trim();
        Title = trimmed.Length <= maxChars ? trimmed : trimmed[..maxChars];
    }

    /// <summary>Records the latest ask's scope (scope is changeable per ask — US-18).</summary>
    public void UpdateScope(ChatScope scope)
    {
        ScopeType = scope.Type;
        ScopeTargetId = scope.TargetId;
    }
}
