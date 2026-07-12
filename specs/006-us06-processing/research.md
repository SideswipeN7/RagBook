# Phase 0 Research — Background Processing (US-06)

## D1 — Durable Wolverine handler + retry policy

- **Decision**: `ProcessDocumentHandler.Handle(DocumentUploaded)` is a Wolverine message handler.
  Configure **WolverineFx.Postgresql** durability (`opts.PersistMessagesWithPostgresql(conn)`) with a
  **local durable queue** for `DocumentUploaded`, so an enqueued/in-flight job survives a restart
  (FR-002). A retry policy (`opts.Policies.OnException<EmbeddingProviderException>().RetryWithCooldown(...)`,
  bounded to `EmbeddingOptions.RetryCount`, default 3, with backoff) covers transient provider errors
  (AC-3). After retries are exhausted Wolverine moves on; the handler's terminal path marks the document
  `Failed(EmbeddingProviderError)`.
- **Rationale**: Reuses the Wolverine already in the host; durability + retry are configuration, not code.
- **Alternatives**: a hand-rolled `BackgroundService` queue (loses durability); Hangfire/Quartz (extra
  infra for one job type).
- **Note**: Wolverine owns its envelope tables (provisioned by Wolverine; dev uses resource setup). This
  is the §VIII deviation recorded in the plan.

## D2 — Session bridge for the background handler

- **Decision**: The handler has no HTTP session. Step 1: `IDocumentProcessingReader.GetTargetAsync(id)`
  reads the document **with `IgnoreQueryFilters`** (by id only) → `(UserSessionId, StoragePath,
  ContentType)`, or `null` if it no longer exists → **stop quietly** (FR-013). Step 2:
  `ISessionInitializer.Initialize(target.UserSessionId)` sets the ambient session for the rest, so chunk
  inserts (`ISessionOwned`, stamped) and the document update are session-scoped (FR-014).
- **Rationale**: The event carries only the id; the owning session must be discovered, then all writes
  flow through the normal session-scoped path (isolation preserved). The bypass is a minimal
  existence+owner read.
- **Alternatives**: carry the session in the event (leaks/pins session identity into the message); run the
  whole pipeline unfiltered (breaks isolation).

## D3 — Structural chunking + normalization

- **Decision**: `StructuralChunker` (pure): normalize first (collapse whitespace, strip control chars
  except tab/newline). Split by structure — Markdown by headings/paragraphs, PDF by page then paragraph
  (the extractor yields `(pageNumber, text)` segments). Greedily pack segments to `ChunkingOptions.TargetChars`
  (~1000), starting each new chunk with `OverlapChars` (~150) of the previous chunk's tail. A very short
  text yields **one** chunk with **no** overlap (FR-005 edge). Each chunk keeps its source `PageNumber`
  (PDF) for citations.
- **Rationale**: Structure-aware chunks retrieve better than fixed windows; overlap preserves context
  across boundaries; config-driven sizes (no magic numbers).
- **Alternatives**: fixed-size char windows (ignores structure); token-based (needs a tokenizer — overkill
  for the case study).

## D4 — Embedding provider: fake + real, batched

- **Decision**: `IEmbeddingProvider.EmbedBatchAsync(IReadOnlyList<string>) → IReadOnlyList<float[]>` +
  `Dimension`. **`FakeEmbeddingProvider`** (dev/tests) produces a **deterministic** unit vector of
  `EmbeddingOptions.Dimension` from a hash of each text (stable, comparable). **`VoyageEmbeddingProvider`**
  calls Voyage `voyage-3.5` over `HttpClient` with the app key. DI selects Voyage when
  `EmbeddingOptions.ApiKey` is set, else the fake. The handler embeds in **batches** of
  `EmbeddingOptions.BatchSize` (default 64) → ⌈N/B⌉ calls (AC-5/SC-005). A Voyage error throws
  `EmbeddingProviderException` (→ Wolverine retry).
- **Rationale**: Full pipeline + deterministic tests without a key; one model/dimension for the whole
  index (FR-006); batching for efficiency and rate limits.
- **Alternatives**: real provider only (no offline tests); per-chunk calls (slow/costly).

## D5 — Chunk storage, idempotence, no partial index

- **Decision**: `Chunk : ISessionOwned` → table `chunks(id, document_id FK ON DELETE CASCADE,
  user_session_id, index, text, page_number NULL, embedding vector(1024))`; **unique `(document_id,
  index)`**; **HNSW** index `USING hnsw (embedding vector_cosine_ops)`. `ChunkRepository.ReplaceForDocumentAsync`
  runs in a transaction: **delete all chunks for the document, then insert** the new set + set the
  document `Ready` + chunk count — so a redelivery/re-run yields the same set (**idempotent**, AC-4/FR-011)
  and a mid-run failure path deletes any partial chunks (**no partial index**, AC-3/FR-012). Cascade FK
  removes chunks when US-08 deletes the document.
- **Rationale**: Delete-then-insert under one transaction is the simplest idempotent write; the unique
  index is a backstop; cascade centralizes chunk cleanup in the DB (US-08 relies on it).
- **Alternatives**: upsert per `(document_id, index)` (leaves stale higher-index chunks when a re-chunk
  produces fewer); app-side dedupe (fragile).

## D6 — pgvector column mapping

- **Decision**: Map `Chunk.Embedding` (`float[]`) via **Pgvector.EntityFrameworkCore** to
  `HasColumnType("vector(1024)")`; enable `o.UseVector()` on the Npgsql data source; the migration runs
  `CREATE EXTENSION IF NOT EXISTS vector;` before the table + HNSW index. Dimension `1024` is fixed in the
  column type (config `EmbeddingOptions.Dimension` MUST match; a change is a new migration + full
  re-index, per README).
- **Rationale**: pgvector is already in the test image; the EF integration gives a typed vector column and
  HNSW index for the US-14 similarity search.
- **Alternatives**: store as `float[]`/bytea (no vector search); a separate vector DB (scope creep).

## D7 — SSE status push

- **Decision**: `IDocumentStatusNotifier` (singleton): `Publish(sessionId, DocumentStatusUpdate)` and
  `Subscribe(sessionId) → IAsyncEnumerable<DocumentStatusUpdate>` backed by per-session `Channel`s. On a
  terminal transition the handler publishes `{documentId, status, chunkCount, failureReason}` for the
  document's session. `GET /api/documents/status/stream` is an **SSE** endpoint: it reads the session from
  the cookie, subscribes, and streams `data:` events until the client disconnects. Angular
  `DocumentStatusStore` opens an `EventSource` and, on each event, patches/`refresh()`es `TreeStore` so the
  row flips `processing → ready/failed` without a reload (FR-016).
- **Rationale**: Push gives immediate status with no polling; in-memory channels are correct for a single
  instance (documented limitation for multi-instance).
- **Alternatives**: polling `GET /api/tree` every 2 s (the story's baseline; not chosen); WebSockets
  (heavier than needed for one-way status).

## D8 — PDF/text extraction + unreadable detection

- **Decision**: `ITextExtractor` per content type via `TextExtractorResolver`: `PlainTextExtractor`
  (TXT/MD, UTF-8 read) and `PdfTextExtractor` (**UglyToad.PdfPig**, page-by-page → `(pageNumber, text)`).
  If extraction throws, or the normalized text is **empty/whitespace** (encrypted/scan/corrupt), the
  handler marks the document `Failed` with a readable reason (AC-2/FR-004) — e.g. "PDF nie zawiera tekstu
  — skany nie są obsługiwane".
- **Rationale**: PdfPig is a mature managed .NET PDF text library (no native deps); empty-text is the
  reliable signal for a scan/encrypted PDF without OCR.
- **Alternatives**: OCR (out of scope); iText (AGPL/license friction).
