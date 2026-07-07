using RagBook.Shared.Sessions;

namespace RagBook.Infrastructure.SharedContext.Sessions;

/// <summary>
/// Scoped holder of the current request's session identity. The middleware initialises it once;
/// handlers and the <c>DbContext</c> read it. One instance backs both <see cref="ISessionContext"/>
/// and <see cref="ISessionInitializer"/> within a request scope.
/// </summary>
public sealed class SessionContext : ISessionContext, ISessionInitializer
{
    /// <inheritdoc />
    public Guid UserSessionId { get; private set; }

    /// <inheritdoc />
    public void Initialize(Guid userSessionId)
    {
        UserSessionId = userSessionId;
    }
}
