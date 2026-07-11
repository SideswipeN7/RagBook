using Microsoft.Extensions.Options;
using RagBook.Modules.Documents.Domain;
using RagBook.Modules.Documents.Errors;
using RagBook.Modules.Documents.Features.UploadDocument;
using RagBook.Shared.Sessions;

namespace RagBook.Modules.Documents.Processing;

/// <summary>
/// Background indexing pipeline (US-06): the Wolverine handler for <see cref="DocumentUploaded"/>. It
/// bridges the ambient session from the document (the worker has no HTTP session), extracts text, chunks
/// it, embeds the chunks in batches (with a bounded retry on transient provider errors), stores the
/// chunks with their vectors, and transitions the document <c>Ready</c> (chunk count) or <c>Failed</c>
/// (reason) — persisting the chunk write and the status transition atomically. Idempotent (chunks are
/// replaced), leaves no partial index on failure, and stops quietly if the document was deleted.
/// </summary>
public sealed class ProcessDocumentHandler(
    IDocumentProcessingReader reader,
    ISessionInitializer sessionInitializer,
    IFileStorage fileStorage,
    IEnumerable<ITextExtractor> textExtractors,
    IChunker chunker,
    IEmbeddingProvider embeddingProvider,
    IChunkRepository chunkRepository,
    IDocumentStatusNotifier notifier,
    IOptions<EmbeddingOptions> embeddingOptions)
{
    /// <summary>Processes one uploaded document end-to-end.</summary>
    public async Task Handle(DocumentUploaded message, CancellationToken cancellationToken)
    {
        ProcessingTarget? target = await reader.GetTargetAsync(message.DocumentId, cancellationToken);
        if (target is null)
        {
            return; // deleted mid-processing — stop quietly (FR-013)
        }

        // Bridge the ambient session so all subsequent reads/writes are correctly session-scoped (FR-014).
        sessionInitializer.Initialize(target.SessionId);

        Document? document = await reader.LoadTrackedAsync(message.DocumentId, cancellationToken);
        if (document is null)
        {
            return; // deleted between the two reads — stop quietly
        }

        ITextExtractor? extractor = textExtractors.FirstOrDefault(candidate => candidate.CanExtract(target.ContentType));
        if (extractor is null)
        {
            await FailAsync(document, ProcessingErrors.TextExtractionFailed, target.SessionId, cancellationToken);
            return;
        }

        ExtractedText extracted;
        try
        {
            await using Stream content = await fileStorage.OpenReadAsync(target.StoragePath, cancellationToken);
            extracted = await extractor.ExtractAsync(content, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await FailAsync(document, ProcessingErrors.TextExtractionFailed, target.SessionId, cancellationToken);
            return;
        }

        IReadOnlyList<TextChunk> textChunks = chunker.Chunk(extracted.Segments);
        if (textChunks.Count == 0)
        {
            await FailAsync(document, ProcessingErrors.TextExtractionFailed, target.SessionId, cancellationToken);
            return;
        }

        List<float[]> vectors;
        try
        {
            vectors = await EmbedAllAsync(textChunks, cancellationToken);
        }
        catch (EmbeddingProviderException)
        {
            await FailAsync(document, ProcessingErrors.EmbeddingProviderError, target.SessionId, cancellationToken);
            return;
        }

        var chunks = textChunks
            .Select((chunk, i) => Chunk.Create(document.Id, chunk.Index, chunk.Text, chunk.PageNumber, vectors[i]))
            .ToList();

        document.MarkReady(chunks.Count);
        await chunkRepository.ReplaceForDocumentAsync(document, chunks, cancellationToken);

        notifier.Publish(
            target.SessionId,
            new DocumentStatusUpdate(document.Id, document.Status.ToString(), document.ChunkCount, null));
    }

    private async Task<List<float[]>> EmbedAllAsync(IReadOnlyList<TextChunk> textChunks, CancellationToken cancellationToken)
    {
        int batchSize = Math.Max(1, embeddingOptions.Value.BatchSize);
        var vectors = new List<float[]>(textChunks.Count);

        for (int start = 0; start < textChunks.Count; start += batchSize)
        {
            List<string> batch = textChunks
                .Skip(start)
                .Take(batchSize)
                .Select(chunk => chunk.Text)
                .ToList();

            vectors.AddRange(await EmbedBatchWithRetryAsync(batch, cancellationToken));
        }

        return vectors;
    }

    private async Task<IReadOnlyList<float[]>> EmbedBatchWithRetryAsync(IReadOnlyList<string> batch, CancellationToken cancellationToken)
    {
        int maxAttempts = Math.Max(1, embeddingOptions.Value.RetryCount);

        for (int attempt = 1; ; attempt++)
        {
            try
            {
                return await embeddingProvider.EmbedBatchAsync(batch, cancellationToken);
            }
            catch (EmbeddingProviderException) when (attempt < maxAttempts)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(200, 25 * attempt)), cancellationToken);
            }
        }
    }

    private async Task FailAsync(Document document, string reason, Guid sessionId, CancellationToken cancellationToken)
    {
        document.MarkFailed(reason);
        await chunkRepository.DeleteForDocumentAsync(document, cancellationToken);

        notifier.Publish(sessionId, new DocumentStatusUpdate(document.Id, document.Status.ToString(), 0, reason));
    }
}
