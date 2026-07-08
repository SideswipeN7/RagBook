namespace RagBook.Modules.Documents.Domain;

/// <summary>
/// How a <see cref="Document"/> entered the session. The quota counts <see cref="User"/> documents
/// (including failed ones) and excludes <see cref="Demo"/> documents — the forward-looking seam for
/// demo mode (US-03), which is not built in US-05.
/// </summary>
public enum DocumentOrigin
{
    /// <summary>Uploaded by the user; counts toward the quota.</summary>
    User = 0,

    /// <summary>Seeded by demo mode (US-03); excluded from the quota.</summary>
    Demo = 1,
}
