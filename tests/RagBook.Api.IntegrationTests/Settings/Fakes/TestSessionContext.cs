using RagBook.Shared.Sessions;

namespace RagBook.Api.IntegrationTests.Settings.Fakes;

/// <summary>Fixed-session <see cref="ISessionContext"/> for directly constructing session-scoped stores in tests.</summary>
public sealed class TestSessionContext(Guid sessionId) : ISessionContext
{
    /// <inheritdoc />
    public Guid UserSessionId { get; } = sessionId;
}
