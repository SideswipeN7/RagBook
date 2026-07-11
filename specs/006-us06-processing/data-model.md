# Phase 1 Data Model — Background Processing (US-06)

## Aggregate: `Chunk` (`Modules/Documents/Domain/Chunk.cs`)

`ISessionOwned` (isolated by the global query filter; stamped from the document's session via the
handler's session-init bridge). One chunk = one indexed slice of a document.

| Property | Type | Notes |
|---|---|---|
| `Id` | `Guid` | Identity. |
| `DocumentId` | `Guid` | Owner; FK → `documents.id` **ON DELETE CASCADE**. |
| `Index` | `int` | 0-based position in the document; unique per `(DocumentId, Index)`. |
| `Text` | `string` | The chunk text (normalized). |
| `PageNumber` | `int?` | Source page (PDF) for citations; null for TXT/MD. |
| `Embedding` | `float[]` | Vector of the configured dimension (`vector(1024)`). |
| `UserSessionId` | `Guid` | Owning session (stamped). |

Factory `Chunk.Create(documentId, index, text, pageNumber, embedding)`. No mutation after creation
(re-indexing replaces the set).

## Aggregate change: `Document` transitions

- `void MarkReady(int chunkCount)` — `Status = Ready`, `ChunkCount = chunkCount`, clears `FailureReason`.
- `void MarkFailed(string reason)` — `Status = Failed`, `FailureReason = reason`, `ChunkCount = 0`.

Only US-06 drives these; the upload leaves the document `Processing` (US-04).

## Seams (`Modules/Documents/Domain/`)

- `IChunkRepository`: `Task ReplaceForDocumentAsync(Document document, IReadOnlyList<Chunk> chunks, CancellationToken)`
  and `Task DeleteForDocumentAsync(Document document, CancellationToken)`. **Both take the tracked
  `Document`** (already `MarkReady`/`MarkFailed`-ed by the handler) and, in **one transaction**, delete the
  document's existing chunks, (for Replace) insert the new chunks, and `SaveChanges` — so the chunk write
  **and** the status transition persist atomically (idempotent, no-partial, U1). The `Document` is tracked
  in the same session-scoped `DbContext`, so its status change is saved with the chunks.
- `IDocumentProcessingReader`: **two** reads —
  `Task<ProcessingTarget?> GetTargetAsync(Guid documentId, ct)` (**session-agnostic**, `IgnoreQueryFilters`,
  by id → `(SessionId, StoragePath, ContentType)`, null if absent) used **before** the session bridge; and
  `Task<Document?> LoadTrackedAsync(Guid documentId, ct)` (**session-scoped, tracked**) used **after**
  `ISessionInitializer.Initialize`, so the handler can apply and persist the transition. Null → the
  document was deleted → stop quietly.
- `ITextExtractor`: `bool CanExtract(string contentType)`; `Task<ExtractedText> ExtractAsync(Stream, ct)`
  → `ExtractedText(IReadOnlyList<ExtractedSegment>)`, `ExtractedSegment(int? PageNumber, string Text)`.
  Throws/returns empty → unreadable.
- `IChunker`: `IReadOnlyList<TextChunk> Chunk(IReadOnlyList<ExtractedSegment> segments)` →
  `TextChunk(int Index, string Text, int? PageNumber)`.
- `IEmbeddingProvider`: `int Dimension`; `Task<IReadOnlyList<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, ct)`.
- `IDocumentStatusNotifier`: `void Publish(Guid sessionId, DocumentStatusUpdate update)`;
  `IAsyncEnumerable<DocumentStatusUpdate> Subscribe(Guid sessionId, CancellationToken)`.

## Options (`Modules/Documents/Processing/`)

- `ChunkingOptions`: `TargetChars = 1000`, `OverlapChars = 150`, `SectionName = "Chunking"`.
- `EmbeddingOptions`: `Model = "voyage-3.5"`, `Dimension = 1024`, `BatchSize = 64`, `RetryCount = 3`,
  `ApiKey = null`, `SectionName = "Embedding"`. `ApiKey` present → Voyage driver; absent → fake.

## Value / message types

- `DocumentStatusUpdate(Guid DocumentId, string Status, int ChunkCount, string? FailureReason)` — the SSE payload.
- `EmbeddingProviderException` — transient provider failure (→ Wolverine retry).

## Persistence — `chunks` table (migration `AddChunks`)

```sql
CREATE EXTENSION IF NOT EXISTS vector;
-- chunks(id uuid pk, document_id uuid FK documents(id) ON DELETE CASCADE, user_session_id uuid,
--        index int, text text, page_number int NULL, embedding vector(1024))
CREATE UNIQUE INDEX ux_chunks_document_index ON chunks (document_id, index);
CREATE INDEX ix_chunks_user_session_id ON chunks (user_session_id);
CREATE INDEX ix_chunks_embedding_hnsw ON chunks USING hnsw (embedding vector_cosine_ops);
```

`ChunkConfiguration` maps `embedding` via `HasColumnType("vector(1024)")` (Pgvector.EntityFrameworkCore);
the functional/HNSW/partial specifics are raw SQL in the migration.

## Rules → requirement trace

| Rule | Where | Requirement |
|---|---|---|
| Background, non-blocking, durable | Wolverine local durable queue | FR-001/FR-002 |
| Extract per type + normalize | `TextExtractorResolver` + extractors | FR-003 |
| Unreadable → Failed + reason, no chunks | handler (empty/throw) → `MarkFailed` | FR-004/AC-2 |
| Structural chunk, size/overlap, ≥1 chunk, page number | `StructuralChunker` | FR-005 |
| One model, config dim; fake+real | `IEmbeddingProvider` + `EmbeddingOptions` | FR-006 |
| Batched embeddings | handler loops in `BatchSize` | FR-007/AC-5 |
| Retry+backoff → Failed(provider) | Wolverine policy + terminal `MarkFailed` | FR-008/AC-3 |
| Chunk store + cascade + unique + vector search | `chunks` DDL | FR-009 |
| Ready + chunk count | `Document.MarkReady` | FR-010/AC-1 |
| Idempotent (replace) | `ReplaceForDocumentAsync` | FR-011/AC-4 |
| No partial index | delete-on-failure / transactional replace | FR-012/AC-3 |
| Deleted mid-run → quiet stop | `GetTargetAsync` null | FR-013 |
| Session-scoped chunks | `ISessionOwned` + session-init bridge | FR-014/SC-007 |
| Config-driven params | `ChunkingOptions`/`EmbeddingOptions` | FR-015 |
| Status push (SSE) → tree updates | notifier + SSE endpoint + `DocumentStatusStore` | FR-016/AC-1 |
