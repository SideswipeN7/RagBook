# Tasks: Background Processing (Przetwarzanie w tle — chunking + embeddingi)

**Input**: Design documents from `specs/006-us06-processing/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/processing-api.md, quickstart.md

**Tests**: Included — the constitution mandates Test-First (Red→Green→Refactor); every behavior lands
via a failing test first, at the cheapest tier that proves it (Domain → Application → Integration).

**Organization**: US-06 is a background pipeline over the US-04 upload event. The `Chunk` aggregate +
transitions, the extractor/chunker/provider/repo/notifier seams, the packages/config, the migration, and
the Wolverine durability wiring are Foundational. Stories: US1 = happy-path index + status push (AC-1),
US2 = unreadable → failed (AC-2), US3 = provider retry → failed, no partial (AC-3), US4 = idempotence +
quiet-stop (AC-4), US5 = batching (AC-5).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel (different files, no incomplete dependency).
- Paths follow plan.md (`src/RagBook/Modules/Documents`, `src/RagBook.API`, `src/RagBook.Infrastructure`, `src/Web`, `tests/…`).

---

## Phase 1: Setup (packages + config)

- [X] T001 Add packages to `Directory.Packages.props` + the consuming csprojs: `WolverineFx.Postgresql` (host), `Pgvector` + `Pgvector.EntityFrameworkCore` (Infrastructure + Migrations), `UglyToad.PdfPig` (Infrastructure).
- [X] T002 [P] Add `Chunking` (`TargetChars`, `OverlapChars`) and `Embedding` (`Model=voyage-3.5`, `Dimension=1024`, `BatchSize=64`, `RetryCount=3`, `ApiKey` empty) sections to `src/RagBook.API/appsettings.json` — config-driven, no magic numbers.

**Checkpoint**: Solution restores with the new packages; config sections present.

---

## Phase 2: Foundational (domain + seams + persistence + messaging — BLOCKS all stories)

### Domain (Red → Green)

- [X] T003 [P] Domain test (Red): `StructuralChunker` — `Should_ProduceSingleChunkNoOverlap_When_ShortText`, `Should_SplitBySizeWithOverlap_When_LongText`, `Should_KeepPageNumbers_When_PdfSegments`, `Should_NormalizeWhitespaceAndStripControlChars` in `tests/RagBook.Domain.Tests/Documents/StructuralChunkerTests.cs`.
- [X] T004 [P] Domain test (Red): `Document` transitions — `Should_MarkReadyWithChunkCount`, `Should_MarkFailedWithReasonAndZeroChunks` in `tests/RagBook.Domain.Tests/Documents/DocumentTransitionsTests.cs`.
- [X] T005 [P] Domain test (Red): `FakeEmbeddingProvider` — `Should_ProduceDeterministicVectorOfConfiguredDimension`, `Should_ReturnOneVectorPerInput` in `tests/RagBook.Domain.Tests/Documents/FakeEmbeddingProviderTests.cs`.
- [X] T006 Implement `Chunk` aggregate (`ISessionOwned`; `Id`, `DocumentId`, `Index`, `Text`, `PageNumber?`, `Embedding float[]`; `Create(...)` factory) in `src/RagBook/Modules/Documents/Domain/Chunk.cs`.
- [X] T007 Add `Document.MarkReady(int chunkCount)` and `Document.MarkFailed(string reason)` in `src/RagBook/Modules/Documents/Domain/Document.cs` (Green for T004).
- [X] T008 Implement `StructuralChunker : IChunker` (normalize; pack segments to `ChunkingOptions.TargetChars` with `OverlapChars`; ≥1 chunk, no overlap when short; keep page number) in `src/RagBook/Modules/Documents/Processing/StructuralChunker.cs` (Green for T003).

### Seams, options, errors

- [X] T009 [P] Define seams in `src/RagBook/Modules/Documents/Domain/`: `IChunkRepository` (`ReplaceForDocumentAsync(Document, chunks)`, `DeleteForDocumentAsync(Document)` — **both take the tracked `Document`** and persist chunks **and** the status transition in one transaction, U1), `ITextExtractor` (+ `ExtractedText`/`ExtractedSegment`), `IChunker` (+ `TextChunk`), `IEmbeddingProvider` (`Dimension`, `EmbedBatchAsync`), `IDocumentProcessingReader` (`GetTargetAsync` session-agnostic **+ `LoadTrackedAsync`** session-scoped tracked, + `ProcessingTarget`), `IDocumentStatusNotifier` (+ `DocumentStatusUpdate`).
- [X] T010 [P] Implement `ChunkingOptions` + `EmbeddingOptions` in `src/RagBook/Modules/Documents/Processing/`; `ProcessingErrors` (reasons: text-extraction-failed, embedding-provider-error) + `EmbeddingProviderException` in `src/RagBook/Modules/Documents/Errors/`; bind options in `Program.cs`.
- [X] T011 [P] Implement `FakeEmbeddingProvider` (deterministic unit vector from text hash, `EmbeddingOptions.Dimension`) in `src/RagBook.Infrastructure/SharedContext/Processing/FakeEmbeddingProvider.cs` (Green for T005).

### Persistence + drivers + messaging

- [X] T012 Implement `ChunkConfiguration` (table `chunks`, `embedding vector(1024)` via Pgvector.EntityFrameworkCore, `user_session_id`, FK `document_id` cascade) + `DbSet<Chunk>` on `RagBookDbContext`; enable `UseVector()` on the Npgsql data source in `AddInfrastructure`.
- [X] T013 Create migration `AddChunks` — `CREATE EXTENSION IF NOT EXISTS vector;` + `chunks` table + FK cascade + `ix_chunks_user_session_id` + raw-SQL `ux_chunks_document_index` and HNSW `ix_chunks_embedding_hnsw (embedding vector_cosine_ops)`; applied out-of-band.
- [X] T014 Implement `ChunkRepository : IChunkRepository` — `ReplaceForDocumentAsync(Document, chunks)` = one transaction: delete-all-chunks-for-document + insert new + `SaveChanges` (persists the tracked document's `MarkReady` too, U1); `DeleteForDocumentAsync(Document)` = delete chunks + `SaveChanges` (persists `MarkFailed`) — in `src/RagBook.Infrastructure/SharedContext/Persistence/ChunkRepository.cs`; register it.
- [X] T015 [P] Implement `DocumentProcessingReader : IDocumentProcessingReader` — `GetTargetAsync` reads by id with `IgnoreQueryFilters` → session/storagePath/contentType (null if absent); `LoadTrackedAsync` returns the **session-scoped tracked** `Document` by id (null if absent) for the transition (U1) — in `src/RagBook.Infrastructure/SharedContext/Persistence/DocumentProcessingReader.cs`; register it.
- [X] T016 [P] Implement `PlainTextExtractor`, `PdfTextExtractor` (UglyToad.PdfPig, page→segment), and `TextExtractorResolver : ITextExtractor` (dispatch by content type) in `src/RagBook.Infrastructure/SharedContext/Processing/`; register the resolver.
- [X] T017 [P] Implement `VoyageEmbeddingProvider` (HttpClient → Voyage `voyage-3.5`, throws `EmbeddingProviderException` on transient failure) in `src/RagBook.Infrastructure/SharedContext/Processing/`; register `IEmbeddingProvider` = Voyage when `Embedding:ApiKey` set, else `FakeEmbeddingProvider`.
- [X] T018 [P] Implement `InMemoryDocumentStatusNotifier : IDocumentStatusNotifier` (per-session `Channel`s; `Publish`/`Subscribe`) as a singleton in `src/RagBook.Infrastructure/SharedContext/Processing/`; register it.
- [X] T019 Configure Wolverine durability in `Program.cs`: `PersistMessagesWithPostgresql(conn)`, a **local durable queue** for `DocumentUploaded`, and a retry policy `OnException<EmbeddingProviderException>().RetryWithCooldown(...)` bounded to `EmbeddingOptions.RetryCount`; ensure envelope storage is provisioned (dev resource setup) — never our migration.

**Checkpoint**: Domain green; chunks schema + vector index exist; drivers + notifier + durable queue wired; provider selectable.

---

## Phase 3: User Story 1 — A document becomes queryable on its own (AC-1) 🎯 MVP

**Independent test**: upload a readable file → after the worker, the document is Ready with a chunk count and its chunks (with vectors) exist; the tree flips via SSE.

- [X] T020 [P] [US1] Application test (Red): `ProcessDocumentHandler` — `Should_ExtractChunkEmbedAndMarkReady_When_Readable` and `Should_PublishStatus_When_Ready` (fakes for reader/storage/extractor/chunker/provider/chunk-repo/notifier; assert chunks saved, `MarkReady(count)`, status published) in `tests/RagBook.Application.Tests/Documents/ProcessDocumentHandlerTests.cs`.
- [X] T021 [US1] Implement `ProcessDocumentHandler.Handle(DocumentUploaded)` — `GetTargetAsync` (agnostic; null → stop) → `ISessionInitializer.Initialize` → `LoadTrackedAsync` (null → stop) → open blob → extract → chunk → embed (batched) → `document.MarkReady(count)` → `ReplaceForDocumentAsync(document, chunks)` (persists both) → publish status — in `src/RagBook/Modules/Documents/Processing/ProcessDocumentHandler.cs` (Green for T020).
- [X] T022 [US1] Implement `GET /api/documents/status/stream` (SSE, session-scoped; subscribes to `IDocumentStatusNotifier`) in `src/RagBook.API/Endpoints/DocumentStatusEndpoints.cs`; map in `Program.cs`.
- [X] T023 [P] [US1] Angular `DocumentStatusStore` (`EventSource` on `/api/documents/status/stream`; on a status event → `TreeStore.refresh()`) in `src/Web/src/app/core/document-status.store.ts` (+ unit test applying an SSE-shaped update to a mocked `TreeStore`); open the stream in `app.ts` on init.
- [X] T024 [US1] Integration test (Red→Green): `Should_IndexDocument_EndToEnd` — upload a readable TXT/PDF, then **resolve `ProcessDocumentHandler` from a DI scope and call `Handle(new DocumentUploaded(id))` directly** (not via the Wolverine bus, so the test needs no envelope infra — F2), assert the document is Ready with a chunk count and `chunks` rows (with vectors) exist — in `tests/RagBook.Api.IntegrationTests/Processing/ProcessingPipelineTests.cs`.

**Checkpoint**: AC-1 demonstrable — an uploaded file indexes itself and the tree flips to Ready. MVP.

---

## Phase 4: User Story 2 — Unreadable file fails clearly (AC-2)

**Independent test**: process a no-text PDF → Failed + readable reason, zero chunks.

- [X] T025 [US2] Application test (Red→Green): `Should_MarkFailed_When_NoExtractableText` (extractor returns empty/throws → `MarkFailed(reason)`, `DeleteForDocumentAsync`, no chunks, status published Failed) in `ProcessDocumentHandlerTests.cs`.
- [X] T026 [US2] Ensure the handler treats empty/whitespace extracted text and extractor exceptions as unreadable → `Document.MarkFailed` with a readable reason (Green); status published so the tree shows the reason + delete action.

**Checkpoint**: AC-2 — unreadable files fail with a reason, nothing indexed.

---

## Phase 5: User Story 3 — Provider blips retry, then fail cleanly (AC-3)

**Independent test**: transient provider error tolerated; permanent error → Failed(provider), no partial chunks.

- [X] T027 [US3] Application test (Red→Green): `Should_Succeed_When_ProviderFailsThenRecovers` and `Should_MarkFailedWithProviderError_And_LeaveNoChunks_When_ProviderKeepsFailing` (fake provider scripted to fail N times / always; assert retry tolerance, terminal Failed(provider), `DeleteForDocumentAsync` leaves zero chunks) in `ProcessDocumentHandlerTests.cs`.
- [X] T028 [US3] Verify the Wolverine retry policy (T019) drives the retries and the handler's terminal path marks `Failed(EmbeddingProviderError)` + deletes partial chunks (Green); confirm no-partial-index.

**Checkpoint**: AC-3 — resilient to blips; clean terminal failure, no partial index.

---

## Phase 6: User Story 4 — Idempotent + quiet stop (AC-4)

**Independent test**: process twice → identical chunk set, no duplicates; deleted document → no-op.

- [X] T029 [US4] Application test (Red→Green): `Should_ProduceIdenticalChunks_When_ProcessedTwice` (two handler runs → `ReplaceForDocumentAsync` yields the same set, no duplicates) and `Should_StopQuietly_When_DocumentDeleted` (`GetTargetAsync` null → no-op, nothing published) in `ProcessDocumentHandlerTests.cs`.
- [X] T030 [US4] Integration test (Red→Green): `Should_ProduceIdenticalChunks_When_ProcessedTwice` end-to-end (call `ProcessDocumentHandler.Handle` **directly** twice for one document — F2; assert `chunks` count unchanged, `(document_id, index)` unique) in `ProcessingPipelineTests.cs`.

**Checkpoint**: AC-4 — safe under at-least-once delivery.

---

## Phase 7: User Story 5 — Batched embeddings (AC-5)

**Independent test**: 200 chunks / batch 64 → 4 provider calls, not 200.

- [X] T031 [US5] Application test (Red→Green): `Should_BatchEmbeddingCalls_When_ManyChunks` — a counting fake provider; assert `EmbedBatchAsync` called ⌈N/BatchSize⌉ times (200/64 → 4), never per-chunk — in `ProcessDocumentHandlerTests.cs`.
- [X] T032 [US5] Ensure the handler batches chunk texts by `EmbeddingOptions.BatchSize` before calling the provider and preserves chunk↔vector order (Green).

**Checkpoint**: AC-5 — embeddings batched.

---

## Phase 8: Cross-cutting integration + docs & polish

- [X] T033 [P] Integration test: `Should_CascadeDeleteChunks_When_DocumentDeleted` (delete a document → its `chunks` rows gone — the FK US-08 relies on) and `Should_NotExposeChunksToAnotherSession` (FR-014) in `tests/RagBook.Api.IntegrationTests/Processing/ProcessingPipelineTests.cs`.
- [X] T034 Update `README.md` — a "Pipeline indeksowania" section: DocumentUploaded → extract → chunk (ChunkingOptions) → embed (voyage-3.5/1024, batched, fake without a key) → chunks(pgvector, HNSW) → Ready/Failed; durable Wolverine queue + retry; **one model for the whole index** (model change = full re-index); SSE status; single-instance SSE/queue note.
- [X] T035 Record durable knowledge in `AGENTS.md` (background handler session-bridge via `IgnoreQueryFilters` + `ISessionInitializer`; `IEmbeddingProvider` fake-vs-Voyage by `Embedding:ApiKey`; `ChunkRepository.ReplaceForDocumentAsync` = idempotence + no-partial; pgvector `vector(1024)` + HNSW; Wolverine durability owns its tables; chunks cascade-delete — US-08).
- [X] T036 Full green run: `dotnet test RagBook.slnx` (Domain + Application + Testcontainers Integration) and `npm test` in `src/Web`; verify `dotnet run --project src/RagBook.AppHost` indexes an uploaded file and the tree flips to Ready via SSE.

---

## Dependencies & execution order

- **Setup (T001–T002)** → **Foundational (T003–T019)** block every story.
- **US1 (T020–T024)** is the MVP (the pipeline + status push). **US2 (T025–T026)**, **US3 (T027–T028)**,
  **US4 (T029–T030)**, **US5 (T031–T032)** all extend the same handler + repo built in US1/Foundational.
- Within a phase, `[P]` tasks touch different files and may run in parallel; test tasks precede their
  implementation. The end-to-end / idempotence / cascade integration tests (T024, T030, T033) follow the
  handler + repository.
- Polish (T034–T036) after all stories are green.

## Parallel example (Foundational)

T003, T004, T005, T009, T010, T011 (`[P]`) touch independent files and can run together; T006/T007/T008
follow their Red tests; T012–T018 (persistence/drivers/notifier) follow the seams; T019 (Wolverine) last.

## MVP scope

**US1 (T001–T024)** yields a demonstrable increment: upload a readable file and watch it index itself and
flip to Ready via SSE. US2–US5 add failure handling, provider resilience, idempotence, and batching.
