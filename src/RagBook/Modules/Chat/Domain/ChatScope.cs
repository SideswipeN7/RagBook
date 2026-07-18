namespace RagBook.Modules.Chat.Domain;

/// <summary>
/// The search boundary for a question (US-13): all documents, a folder (with its subtree), or a single
/// document. Constructed only through the factories, so an invalid combination (e.g. <see cref="ChatScopeType.All"/>
/// with a target, or a folder/document without one) is unrepresentable. Validation of the target against
/// the session happens later, in the retriever (a not-visible target → <c>chat.scope_not_found</c>).
/// </summary>
public sealed record ChatScope
{
    private ChatScope(ChatScopeType type, Guid? targetId)
    {
        Type = type;
        TargetId = targetId;
    }

    /// <summary>Which boundary this scope selects.</summary>
    public ChatScopeType Type { get; }

    /// <summary>The folder or document id for <see cref="ChatScopeType.Folder"/>/<see cref="ChatScopeType.Document"/>; <c>null</c> for <see cref="ChatScopeType.All"/>.</summary>
    public Guid? TargetId { get; }

    /// <summary>Scope over every ready document in the session.</summary>
    public static ChatScope All()
    {
        return new ChatScope(ChatScopeType.All, targetId: null);
    }

    /// <summary>Scope over the folder <paramref name="folderId"/> and its whole subtree.</summary>
    public static ChatScope Folder(Guid folderId)
    {
        return new ChatScope(ChatScopeType.Folder, folderId);
    }

    /// <summary>Scope over the single document <paramref name="documentId"/>.</summary>
    public static ChatScope Document(Guid documentId)
    {
        return new ChatScope(ChatScopeType.Document, documentId);
    }

    /// <summary>Scope over the globally-visible demo documents (US-03) — no target; retrieved by origin, not session.</summary>
    public static ChatScope Demo()
    {
        return new ChatScope(ChatScopeType.Demo, targetId: null);
    }
}
