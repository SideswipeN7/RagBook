using RagBook.Shared.Sessions;

namespace RagBook.Infrastructure.SharedContext.Sessions;

/// <summary>
/// Holder of the current request's session identity. The middleware initialises it once; handlers and
/// the <c>DbContext</c> read it. The value is stored in an <see cref="AsyncLocal{T}"/> so it flows with
/// the request's async execution context — Wolverine handles messages in a separate DI scope, so a
/// plain scoped field set by the middleware would not reach the handler's <c>DbContext</c>.
/// </summary>
public sealed class SessionContext : ISessionContext, ISessionInitializer
{
    private static readonly AsyncLocal<Guid> Current = new();

    /// <inheritdoc />
    public Guid UserSessionId => Current.Value;

    /// <inheritdoc />
    public void Initialize(Guid userSessionId)
    {
        Current.Value = userSessionId;
    }
}
