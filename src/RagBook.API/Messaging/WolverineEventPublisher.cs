using RagBook.Shared.Messaging;
using Wolverine;

namespace RagBook.API.Messaging;

/// <summary>
/// Wolverine-backed <see cref="IEventPublisher"/>. Lives in the web host (which owns Wolverine) so the
/// core application layer can publish in-process events through the <see cref="IEventPublisher"/>
/// abstraction without referencing the messaging infrastructure.
/// </summary>
public sealed class WolverineEventPublisher(IMessageBus messageBus) : IEventPublisher
{
    /// <inheritdoc />
    public Task PublishAsync(IEvent @event, CancellationToken cancellationToken)
    {
        return messageBus.PublishAsync(@event).AsTask();
    }
}
