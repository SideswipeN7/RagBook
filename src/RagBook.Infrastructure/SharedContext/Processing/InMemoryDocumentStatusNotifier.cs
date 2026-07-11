using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using RagBook.Modules.Documents.Domain;

namespace RagBook.Infrastructure.SharedContext.Processing;

/// <summary>
/// In-memory, per-session publish/subscribe for document status changes (US-06), backing the SSE
/// endpoint. A singleton holding one channel per active subscriber; <see cref="Publish"/> fans out to the
/// session's subscribers, <see cref="Subscribe"/> streams until the client disconnects. Single-instance
/// (documented limitation — a multi-instance deployment would need a shared bus).
/// </summary>
public sealed class InMemoryDocumentStatusNotifier : IDocumentStatusNotifier
{
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<Guid, Channel<DocumentStatusUpdate>>> _sessions = new();

    /// <inheritdoc />
    public void Publish(Guid sessionId, DocumentStatusUpdate update)
    {
        if (_sessions.TryGetValue(sessionId, out ConcurrentDictionary<Guid, Channel<DocumentStatusUpdate>>? subscribers))
        {
            foreach (Channel<DocumentStatusUpdate> channel in subscribers.Values)
            {
                channel.Writer.TryWrite(update);
            }
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<DocumentStatusUpdate> Subscribe(
        Guid sessionId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var channel = Channel.CreateUnbounded<DocumentStatusUpdate>();
        var subscriptionId = Guid.NewGuid();
        ConcurrentDictionary<Guid, Channel<DocumentStatusUpdate>> subscribers =
            _sessions.GetOrAdd(sessionId, _ => new ConcurrentDictionary<Guid, Channel<DocumentStatusUpdate>>());
        subscribers[subscriptionId] = channel;

        try
        {
            await foreach (DocumentStatusUpdate update in channel.Reader.ReadAllAsync(cancellationToken))
            {
                yield return update;
            }
        }
        finally
        {
            subscribers.TryRemove(subscriptionId, out _);
        }
    }
}
