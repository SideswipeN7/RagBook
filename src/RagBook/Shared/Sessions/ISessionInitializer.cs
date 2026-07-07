namespace RagBook.Shared.Sessions;

/// <summary>
/// Writes the resolved session identity into the ambient <see cref="ISessionContext"/> for the
/// current request. Called once, early, by the session middleware — never by handlers.
/// </summary>
public interface ISessionInitializer
{
    /// <summary>Sets the current request's <paramref name="userSessionId"/>.</summary>
    void Initialize(Guid userSessionId);
}
