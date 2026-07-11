# Contract — Processing pipeline + status stream (US-06)

Most of US-06 is an internal background pipeline (no HTTP). The only HTTP surface is the SSE status stream.

## Trigger (internal) — `DocumentUploaded` handler

`ProcessDocumentHandler.Handle(DocumentUploaded evt)` (Wolverine, durable local queue). Pipeline:

1. `IDocumentProcessingReader.GetTargetAsync(evt.DocumentId)` (session-agnostic) → target or **null →
   stop quietly** (deleted).
2. `ISessionInitializer.Initialize(target.SessionId)` — scope the rest to the owning session.
3. `IFileStorage.OpenReadAsync(target.StoragePath)` → bytes.
4. `ITextExtractor` (resolved by `ContentType`) → segments; empty/throw → `Document.MarkFailed(reason)` +
   `IChunkRepository.DeleteForDocumentAsync` → save → publish status → **done**.
5. `IChunker.Chunk(segments)` → chunks (≥1; page numbers preserved).
6. `IEmbeddingProvider.EmbedBatchAsync` in batches of `EmbeddingOptions.BatchSize` → vectors. A transient
   error throws `EmbeddingProviderException` → **Wolverine retry** (bounded); exhausted → terminal
   `MarkFailed(EmbeddingProviderError)` + delete partial.
7. `IChunkRepository.ReplaceForDocumentAsync(documentId, chunks)` (transactional delete+insert) +
   `Document.MarkReady(chunkCount)` → save.
8. `IDocumentStatusNotifier.Publish(sessionId, {documentId, status, chunkCount, failureReason})`.

Idempotent: a redelivery repeats the replace → identical chunk set, no duplicates.

## GET `/api/documents/status/stream` — SSE status channel

Session-scoped by cookie. `Content-Type: text/event-stream`. Streams a `data:` event per status change
for the current session until the client disconnects:

```text
event: status
data: {"documentId":"<guid>","status":"Ready","chunkCount":8,"failureReason":null}

data: {"documentId":"<guid>","status":"Failed","chunkCount":0,"failureReason":"PDF nie zawiera tekstu — skany nie są obsługiwane"}
```

- Only the current session's updates are delivered (isolation).
- The client (`DocumentStatusStore`, `EventSource`) patches/`refresh()`es `TreeStore` on each event, so the
  tree row flips `Processing → Ready/Failed` without a reload.
- Best-effort: if the stream drops, the tree still reflects the true state on the next read (`GET /api/tree`).

## Internal seams (not HTTP)

- `IChunkRepository` — transactional `ReplaceForDocumentAsync` (idempotent) / `DeleteForDocumentAsync`.
- `ITextExtractor` / `IChunker` / `IEmbeddingProvider` (fake + Voyage) — the pure/driver pieces.
- `IDocumentProcessingReader` — session-agnostic `GetTargetAsync`.
- `IDocumentStatusNotifier` — per-session in-memory publish/subscribe backing the SSE endpoint.

## Config

- `Chunking`: `TargetChars` (1000), `OverlapChars` (150).
- `Embedding`: `Model` (voyage-3.5), `Dimension` (1024), `BatchSize` (64), `RetryCount` (3), `ApiKey`
  (absent → fake provider). No magic numbers.
