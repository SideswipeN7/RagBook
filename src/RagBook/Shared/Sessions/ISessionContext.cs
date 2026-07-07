namespace RagBook.Shared.Sessions;

/// <summary>
/// Ambient identity of the current anonymous session. Injected into handlers and the persistence
/// layer so every read is scoped to <see cref="UserSessionId"/> (constitution §III).
/// </summary>
public interface ISessionContext
{
    /// <summary>The current visitor's session identifier (GUID v4).</summary>
    Guid UserSessionId { get; }
}
