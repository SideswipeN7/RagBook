namespace RagBook.Modules.Chat.Domain;

/// <summary>The boundary a question searches within (US-13). Compared as an int in the retrieval query.</summary>
public enum ChatScopeType
{
    /// <summary>Every ready document in the session.</summary>
    All = 0,

    /// <summary>A folder and its whole subtree.</summary>
    Folder = 1,

    /// <summary>A single document.</summary>
    Document = 2,

    /// <summary>Every ready demo document, across all sessions (US-03) — retrieved by origin, not session.</summary>
    Demo = 3,
}
