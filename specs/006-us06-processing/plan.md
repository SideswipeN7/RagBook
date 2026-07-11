# Implementation Plan: Background Processing (US-06)

**Branch**: `006-us06-processing` | **Date**: 2026-07-11 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/006-us06-processing/`

## Summary

US-06 adds the **background indexing pipeline**. A durable Wolverine handler reacts to the US-04
`DocumentUploaded` event: it reads the blob (`IFileStorage`), extracts text (`ITextExtractor` per type —
PdfPig for PDF, direct read for TXT/MD, with whitespace/control-char normalization), splits it into
overlapping **structural** chunks (`IChunker`, config-driven size/overlap, page number preserved),
generates embeddings in **batches** through `IEmbeddingProvider` (a deterministic **fake** for dev/tests
+ a real **Voyage `voyage-3.5`/1024** driver), stores the chunks with their vectors (**pgvector**), and
transitions the document `Processing → Ready` (with chunk count) or `Processing → Failed` (with a reason).
Processing is **idempotent** (chunks replaced per document; unique `(document_id, index)`), leaves **no
partial index** on failure, **retries** transient provider errors with backoff, and survives restarts
(durable inbox on Postgres). The document's status change is pushed to the UI over **SSE**. Depends on
US-04 (upload + event) and US-07 (`FailureReason`, status-aware tree).

Key design point: the background handler has **no HTTP session**. It first loads the document
**session-agnostically** (bypassing the query filter) by id to get its `UserSessionId`, `StoragePath`,
and `ContentType`; if absent it **stops quietly** (deleted mid-processing). It then initializes the
ambient session (`ISessionInitializer`) to the document's session, so all subsequent reads/writes —
chunk inserts (stamped `ISessionOwned`) and the document update — are correctly session-scoped and
isolated (FR-014).

## Technical Context

**Language/Version**: C# (LangVersion `preview`) on **.NET 10**; TypeScript ~5.8 (Angular 20)

**Primary Dependencies**: **Wolverine** (already used) + **WolverineFx.Postgresql** (durable inbox/outbox
+ retry policy); **Pgvector** + **Pgvector.EntityFrameworkCore** (vector column + HNSW); **UglyToad.PdfPig**
(PDF text extraction); a Voyage AI embeddings driver over `HttpClient` (no package). Angular `EventSource`
(SSE) — no new frontend package.

**Storage**: PostgreSQL + **pgvector** (the Testcontainers image is already `pgvector/pgvector:pg17`).
New table `chunks(id, document_id FK ON DELETE CASCADE, user_session_id, index, text, page_number NULL,
embedding vector(1024))`; unique `(document_id, index)`; **HNSW** index `vector_cosine_ops`. The `vector`
extension is enabled by migration. Wolverine's durability tables are provisioned by Wolverine (see
Complexity Tracking).

**Testing**: xUnit + FluentAssertions — Domain (`Chunker` structural/overlap/short-file, `Document`
transitions, `FakeEmbeddingProvider` determinism/dimension), Application (`ProcessDocumentHandler` with
fakes: happy path, unreadable → failed, provider-error → failed + no partial, idempotent re-run,
batching count), Integration (Testcontainers: end-to-end index a real TXT/PDF → chunks+vectors + Ready;
cascade delete; session isolation). Angular unit tests (SSE status store updates the tree).

**Target Platform**: Linux container → GCP Cloud Run (single instance for the case study — see SSE/durability notes).

**Project Type**: Web application (Angular SPA + ASP.NET Core modular monolith), Aspire-orchestrated.

**Performance Goals**: Not latency-critical (background). Embeddings **batched** (config size, default 64)
→ ⌈N/B⌉ provider calls (SC-005). Documents bounded by the upload quota.

**Constraints**: config-driven params (`ChunkingOptions`, `EmbeddingOptions` — model/dimension/batch/retry)
— no magic numbers; **one model** for the whole index (indexing = querying, US-14); idempotent + no partial
index; session isolation (chunks `ISessionOwned`); durable queue; errors as `Failed`+reason, never a crash
loop; migrations out-of-band (except Wolverine's own envelope storage).

**Scale/Scope**: Case-study scale, **single instance**. The in-memory SSE notifier and Wolverine's local
durable queue assume one process; multi-instance fan-out is out of scope (noted).

## Constitution Check

| Principle | US-06 compliance |
|---|---|
| **I. Vertical-slice modular monolith** | New `Documents/Processing/` slice: the `DocumentUploaded` handler + `ITextExtractor`/`IChunker`/`IEmbeddingProvider`/`IChunkRepository` seams + the `Chunk` aggregate. Reacts to an event (no cross-module call). ✅ |
| **II. CQRS + Result contract** | The processing handler is an event handler (not a command); failures are captured as `Document.MarkFailed(reason)` (domain state), not thrown to the user. Transient errors surface as exceptions for Wolverine's retry, then a terminal `Failed`. `Permissions/` deferred. ✅ (justified deviation) |
| **III. Data isolation by session** | `Chunk : ISessionOwned` (stamped, global query filter); the handler initializes the ambient session from the document before any write, so chunks and the update are session-scoped (FR-014). The initial session-agnostic read is by id only, to discover the owning session. ✅ |
| **IV. Test-first** | Domain (chunker/transitions/fake provider), Application (handler with fakes — all 5 ACs incl. idempotence + batching count), Integration (Testcontainers end-to-end + cascade + isolation). ✅ |
| **V. Provider resilience + cache** | `IEmbeddingProvider` abstraction with a real Voyage driver + deterministic fake; **retry with backoff** (Wolverine policy, bounded); a transient blip does not permanently fail a document (AC-3). This is the story that realizes §V. ✅ |
| **VI. Auditing & time** | `Chunk` is `ISessionOwned` (audit kept light — chunks are bulk); document transitions stamp `ModifiedAt/By` via the existing interceptor + `TimeProvider`. ✅ |
| **VII. Secrets** | The Voyage **application key** is read from configuration/Secret Manager (never DB, never logged); absent key → the fake provider (dev/tests). Config-driven model/dimension. ✅ |
| **VIII. Operations & delivery** | Migration `AddChunks` (vector extension + table + HNSW + FK cascade) applied out-of-band. **Wolverine provisions its own durability tables** — see Complexity Tracking. ✅ (justified) |
| **IX. Frontend & design system** | No new component — the US-07 tree already renders the status badge/spinner/error+reason; US-06 adds an SSE `DocumentStatusStore` that updates `TreeStore` on push. Standalone/signals; graceful when SSE is unavailable (the tree still reads on demand). ✅ |

**Gate result: PASS** with justified deviations (`Permissions/` deferred; Wolverine-managed durability storage).

## Project Structure

```text
src/
├── RagBook/                                          # Core
│   └── Modules/Documents/
│       ├── Domain/
│       │   ├── Document.cs                            # + MarkReady(chunkCount) / MarkFailed(reason) transitions
│       │   ├── Chunk.cs                               # ISessionOwned; DocumentId, Index, Text, PageNumber?, Embedding(float[])
│       │   ├── IChunkRepository.cs                    # ReplaceForDocumentAsync (idempotent delete+insert), DeleteForDocumentAsync
│       │   ├── ITextExtractor.cs + IExtractedText     # per content type; TextExtractionFailed for unreadable
│       │   ├── IChunker.cs + TextChunk                # structural chunking (paragraphs/pages, size/overlap, page number)
│       │   ├── IEmbeddingProvider.cs                  # EmbedBatchAsync(IReadOnlyList<string>) → vectors; Dimension
│       │   ├── IDocumentProcessingReader.cs           # session-agnostic GetTargetAsync(id) → (sessionId, storagePath, contentType)
│       │   └── IDocumentStatusNotifier.cs             # Publish(sessionId, statusUpdate) + Subscribe(sessionId) (SSE seam)
│       ├── Processing/
│       │   ├── ProcessDocumentHandler.cs              # Wolverine handler on DocumentUploaded — the pipeline
│       │   ├── ChunkingOptions.cs                     # TargetChars, OverlapChars, SectionName
│       │   ├── EmbeddingOptions.cs                    # Model, Dimension, BatchSize, RetryCount, ApiKey?, SectionName
│       │   ├── StructuralChunker.cs                   # IChunker impl (pure, testable)
│       │   └── DocumentStatusUpdate.cs                # {DocumentId, Status, ChunkCount, FailureReason}
│       └── Errors/ProcessingErrors.cs                 # document.text_extraction_failed, document.embedding_provider_error (reasons)
├── RagBook.API/
│   ├── Program.cs                                     # Wolverine durability + retry policy; Configure options; MapDocumentStatusStream()
│   ├── Endpoints/DocumentStatusEndpoints.cs          # GET /api/documents/status/stream (SSE, session-scoped)
│   └── Messaging/ (WolverineEventPublisher exists)
├── RagBook.Infrastructure/
│   ├── DependencyInjection.cs                         # bind extractors/chunker/provider/repos/notifier; provider = Voyage if key else Fake
│   └── SharedContext/
│       ├── Persistence/
│       │   ├── Configurations/ChunkConfiguration.cs   # vector(1024), (document_id,index) unique, FK cascade
│       │   ├── ChunkRepository.cs                     # ReplaceForDocumentAsync in a transaction (idempotent)
│       │   └── DocumentProcessingReader.cs            # IgnoreQueryFilters read by id
│       ├── Processing/
│       │   ├── PdfTextExtractor.cs / PlainTextExtractor.cs / TextExtractorResolver.cs
│       │   ├── VoyageEmbeddingProvider.cs (HttpClient) / FakeEmbeddingProvider.cs (deterministic)
│       │   └── InMemoryDocumentStatusNotifier.cs      # per-session channels (singleton)
├── RagBook.Infrastructure.Migrations/Migrations/      # AddChunks (CREATE EXTENSION vector; chunks table; HNSW; FK cascade)
└── Web/src/app/
    ├── core/document-status.store.ts                 # EventSource /api/documents/status/stream → TreeStore.refresh()/patch
    └── app.ts                                         # open the SSE stream on init
tests/
├── RagBook.Domain.Tests/Documents/                    # ChunkerTests, DocumentTransitionsTests, FakeEmbeddingProviderTests
├── RagBook.Application.Tests/Documents/               # ProcessDocumentHandlerTests (fakes: 5 ACs)
└── RagBook.Api.IntegrationTests/Processing/           # ProcessingPipelineTests (end-to-end, cascade, isolation)
```

**Structure Decision**: the pipeline is a `Documents/Processing/` slice with pure domain pieces
(`StructuralChunker`, transitions, fake provider) and Infrastructure drivers (PdfPig, Voyage, pgvector,
SSE notifier). The handler owns orchestration + the session-init bridge; idempotence lives in
`ChunkRepository.ReplaceForDocumentAsync`; resilience in the Wolverine retry policy.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| **Wolverine provisions its own durability tables** (not our out-of-band migration, §VIII) | Durable inbox/outbox is Wolverine-owned schema; it manages/migrates it. Forcing it into our migration project would fork Wolverine's schema ownership. | Hand-writing Wolverine's envelope tables is brittle across Wolverine versions; auto-provisioning (dev) + documented ops step is the pragmatic path. |
| New packages (`WolverineFx.Postgresql`, `Pgvector(.EntityFrameworkCore)`, `UglyToad.PdfPig`) | Durable messaging, the vector column/index, and PDF extraction have no in-repo equivalent. | Rolling our own durable queue / vector storage / PDF parser is out of scope and error-prone. |
| **Session-agnostic initial read** (`IgnoreQueryFilters`) then `ISessionInitializer` | The background handler has no HTTP session; it must discover the document's session to scope the rest. The bypass is a by-id existence+owner read only. | Threading a session through the event is impossible (the event carries only the id); a globally unfiltered pipeline would break isolation. |
| **In-memory SSE notifier** (single instance) | Case-study runs one instance; per-session in-memory channels are the simplest correct push. | A cross-instance bus (Redis/NOTIFY) is scope creep for a single-instance deployment (documented limitation). |
| `Documents` module still ships **no `Permissions/`** (§II) | Anonymous sessions; processing applies to the owning session only. | Empty scaffolding; deferred consistently. |

## Phase notes

- **Phase 0 (research.md)** — decisions: Wolverine durable local queue + retry policy; the session-init
  bridge; structural chunking algorithm + normalization; embedding batching + fake/real provider; pgvector
  column/HNSW + `ReplaceForDocument` idempotence + no-partial-index; SSE notifier + `EventSource` client;
  PdfPig extraction + unreadable detection.
- **Phase 1 (data-model.md, contracts/, quickstart.md)** — `Chunk` + transitions + seams + options; the
  `chunks` DDL + vector index; the `DocumentUploaded` handler contract + the SSE stream contract; the
  runnable quickstart proving AC-1..AC-5.
- **Phase 2 (tasks.md)** — `/speckit-tasks`, Red→Green→Refactor; the idempotence, retry/no-partial, and
  end-to-end integration tests land with their code.
