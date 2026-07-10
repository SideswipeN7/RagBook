namespace RagBook.Shared.Messaging;

/// <summary>
/// Publishes an in-process <see cref="IEvent"/> without the core application layer referencing the
/// messaging infrastructure (Wolverine lives in the web host). Handlers depend on this abstraction; the
/// host provides the implementation (constitution §I — Core depends on abstractions only).
/// </summary>
public interface IEventPublisher
{
    /// <summary>Publishes <paramref name="event"/> for in-process subscribers.</summary>
    Task PublishAsync(IEvent @event, CancellationToken cancellationToken);
}
