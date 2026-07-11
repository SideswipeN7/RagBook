# Feature Specification: Background Processing (Przetwarzanie w tle — chunking + embeddingi)

**Feature Branch**: `006-us06-processing`

**Created**: 2026-07-11

**Status**: Draft

**Input**: US-06 — After a file is uploaded, the visitor watches the document move `Processing → Ready`
(or `Failed`, with a reason) and, once ready, can ask questions about it — without the interface
blocking. Processing runs in the **background**: on the `DocumentUploaded` event (published by US-04),
a durable in-process worker extracts the text, splits it into overlapping structural chunks, generates
embeddings for those chunks through a **centralized** provider (one model for the whole index), and
stores the chunks with their vectors; it then marks the document `Ready` with its chunk count, or
`Failed` with a human-readable reason. Redelivery is safe (no duplicate chunks); provider blips retry
with backoff; embedding calls are batched. Depends on US-04 (upload + event) and US-07 (the
`FailureReason` field and the status-aware tree). Cross-cutting decisions from the README + constitution
apply — config-driven parameters, one embedding model for indexing and querying, session isolation.

## Clarifications

### Session 2026-07-11

Most decisions are fixed by US-06 "Kontekst / decyzje projektowe" and the README and are not re-opened:

- **Trigger**: a durable, in-process worker reacts to the US-04 `DocumentUploaded` event; the queue is
  persisted so a restart does not lose work.
- **Chunking**: structural (paragraphs/headings for Markdown; pages+paragraphs for PDF), target ~800–1200
  characters with ~150-character overlap, all config-driven; the source **page number** is preserved for
  later citations.
- **Embeddings**: **centralized** (an application key, not the user's BYOK key), **one model for the
  entire index** — indexing and querying MUST use the same model; behind an `IEmbeddingProvider`
  abstraction with the vector dimension in config; changing the model means a full re-index.
- **Extraction**: PDF via a .NET library; TXT/MD read directly; one extractor per type.
- **Storage**: chunks carry `document_id` (cascade-deletes with the document), an index, the text, an
  optional page number, and the embedding vector; unique per `(document_id, index)`; a vector index for
  similarity search.
- **Status & errors**: this story owns the `Processing → Ready/Failed` transitions on the document and
  fills its chunk count and (on failure) its reason.

Three points genuinely needed product input and were resolved this session:

- Q: How is the embedding provider implemented given no provider key in dev? → A: **A deterministic
  stand-in provider for dev/tests** (produces a stable vector of the configured dimension from the text)
  **plus a real Voyage AI driver behind the same `IEmbeddingProvider`**, selected when a key is
  configured. The full pipeline and its tests run without a key.
- Q: How does the UI learn of a status change without a reload? → A: **A server push (SSE) channel** — the
  client subscribes and receives `ready`/`failed` updates as they happen (not polling).
- Q: Which embedding model and vector dimension? → A: **`voyage-3.5`, dimension `1024`**, both
  config-driven; the stand-in uses the same dimension so the index stays query-comparable.

Remaining implementation details (the PDF library, the retry-policy wiring, the batch-size default, the
SSE notification plumbing) are deferred to `plan.md`.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A document becomes queryable on its own (Priority: P1)

A visitor uploads a readable file and, without doing anything else, sees it move from *processing* to
*ready* with a chunk count; from then on its content is part of the searchable index.

**Why this priority**: This is the feature — turning an uploaded file into indexed, queryable knowledge
is the whole point of the pipeline; every RAG story downstream depends on it.

**Independent Test**: Upload a readable text/PDF, wait for the background worker, and confirm the
document is *ready* with a chunk count and that its chunks (with vectors) exist in the index.

**Acceptance Scenarios**:

1. **Given** a document in *processing*, **When** the worker finishes extraction, chunking, and
   embeddings, **Then** the chunks with their vectors are stored, the document is *ready*, and its chunk
   count is set.
2. **Given** the worker finished, **When** the visitor is viewing the tree, **Then** the status changes
   from *processing* to *ready* without a page reload.

---

### User Story 2 - A file that can't be read fails clearly (Priority: P1)

A visitor uploads a PDF with no extractable text (encrypted, corrupt, or a pure scan). The document ends
in *failed* with a plain-language reason, and the visitor can delete it.

**Why this priority**: Silent failures or endless spinners erode trust; the visitor must know *why* a
file did not index and be able to act.

**Independent Test**: Upload a PDF with no text layer and confirm the document ends *failed* with a
readable reason (e.g. "the PDF has no text — scans are not supported"), and no chunks are stored.

**Acceptance Scenarios**:

1. **Given** a PDF that yields no extractable text, **When** the worker tries to extract it, **Then** the
   document is *failed* with a human-readable reason and no chunks are stored.
2. **Given** a failed document, **When** the visitor views it, **Then** the reason is shown and a delete
   action is available.

---

### User Story 3 - Provider blips retry, then fail cleanly (Priority: P1)

The embedding provider times out or errors transiently. The worker retries a few times with backoff; if
it still can't finish, the document ends *failed* with a provider-error reason and **no partial index**
is left behind.

**Why this priority**: External providers are unreliable; a transient blip must not permanently break a
document, and a permanent failure must not leave half a document in the index (which would corrupt
answers).

**Independent Test**: Make the provider fail transiently, then permanently; confirm the worker retries,
then marks the document *failed* with a provider-error reason, and that no chunks from the failed run
remain.

**Acceptance Scenarios**:

1. **Given** a transient provider error, **When** the worker processes the document, **Then** it retries
   with backoff (bounded number of attempts) before giving up.
2. **Given** the retries are exhausted, **When** the worker gives up, **Then** the document is *failed*
   with a provider-error reason and any partial chunks from that run are removed.

---

### User Story 4 - Safe to process the same upload twice (Priority: P1)

The same "uploaded" signal is delivered more than once (at-least-once delivery), or a re-run happens
after a restart. The end result is identical — the document is indexed exactly once, with no duplicate
chunks.

**Why this priority**: A durable queue guarantees at-least-once delivery; without idempotence, redelivery
would double-index a document and corrupt retrieval.

**Independent Test**: Deliver the processing signal twice for the same document; confirm the final chunk
set is identical with no duplicates.

**Acceptance Scenarios**:

1. **Given** a document already *ready* (or with existing chunks), **When** the processing signal is
   delivered again, **Then** the final result is identical — no duplicate chunks are created.
2. **Given** a document deleted while it was being processed, **When** the worker reaches the point of
   saving, **Then** it stops quietly (there is nothing to update).

---

### User Story 5 - Embeddings are batched, not per-chunk (Priority: P1)

A large document with many chunks is embedded through a small number of batched provider calls, not one
call per chunk.

**Why this priority**: Per-chunk calls are slow and costly and risk rate limits; batching keeps
processing efficient and within provider limits.

**Independent Test**: Process a document that produces ~200 chunks and confirm the provider is called in
batches (e.g. groups of a configured size), not ~200 times.

**Acceptance Scenarios**:

1. **Given** a document producing 200 chunks and a batch size of 64, **When** embeddings are generated,
   **Then** the provider is called about 4 times (⌈200 ÷ 64⌉), not 200 times.

---

### Edge Cases

- **Document deleted mid-processing** → the worker stops quietly; no orphaned chunks, no error surfaced.
- **Very short file (one paragraph)** → at least one chunk is produced, with no overlap.
- **Odd encoding / control characters in extracted text** → whitespace is normalized and control
  characters stripped before chunking.
- **Empty extractable text** (a file that reads as blank) → treated as unreadable → *failed* with a
  reason, no chunks.
- **Restart mid-queue** → because the queue is durable, the pending document is processed after restart
  (not lost), and idempotence keeps the result correct.
- **Cross-session** → a document is processed and indexed only for its owning session; chunks are never
  visible to another session.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: On the "document uploaded" signal, the system MUST process the document in the
  **background** without blocking the uploader; the interface stays responsive.
- **FR-002**: The processing queue MUST be **durable** so that a restart does not lose queued or
  in-flight work (the document is processed after restart).
- **FR-003**: The system MUST extract text per file type — PDF via a text-extraction library, TXT/MD read
  directly — and MUST normalize whitespace and strip control characters from the extracted text.
- **FR-004**: When no text can be extracted (encrypted/corrupt/scan-only PDF, or blank content), the
  document MUST be marked **failed** with a human-readable reason, and **no chunks** stored.
- **FR-005**: The system MUST split the text into **structural** chunks (paragraphs/headings for
  Markdown; pages+paragraphs for PDF) targeting a configured size with a configured overlap; a very short
  text MUST still yield at least one chunk (no overlap). Every chunk MUST retain its source **page
  number** when available (for later citations).
- **FR-006**: The system MUST generate embeddings through a **centralized** provider abstraction using a
  **single configured model** (`voyage-3.5`, dimension `1024`, both config-driven) for the whole index;
  indexing MUST use the same model the query side will use. The provider abstraction MUST have **a real
  driver and a deterministic stand-in** (same configured dimension), the stand-in used when no provider
  key is configured (dev/tests).
- **FR-007**: Embedding requests MUST be **batched** (a configured batch size), not one call per chunk.
- **FR-008**: On transient provider failures, the system MUST **retry with backoff** up to a bounded
  number of attempts; if still failing, the document MUST be marked **failed** with a provider-error
  reason.
- **FR-009**: The system MUST store each chunk with its `document_id`, index, text, optional page number,
  and embedding vector; chunks MUST be **deleted automatically when their document is deleted** (cascade);
  each chunk MUST be unique per `(document_id, index)`; the store MUST support similarity search over the
  vectors.
- **FR-010**: On success the system MUST mark the document **ready** and set its **chunk count**.
- **FR-011**: Processing MUST be **idempotent**: a redelivered signal (or a re-run) MUST yield the same
  final chunk set with no duplicates (existing chunks for the document are cleared before a fresh write,
  and/or the `(document_id, index)` uniqueness prevents duplicates).
- **FR-012**: A failed run MUST leave **no partial index** — any chunks written before a failure are
  removed, so a failed document has zero chunks.
- **FR-013**: If the document no longer exists when the worker runs (deleted mid-processing), the worker
  MUST stop **quietly** without error and without leaving orphaned chunks.
- **FR-014**: Processing and its stored chunks MUST be scoped to the owning session — a document is
  indexed only for its session, and its chunks are never visible to another session.
- **FR-015**: All processing parameters MUST be **config-driven** (chunk size, overlap, batch size, the
  embedding model + vector dimension, the retry count) — no magic numbers.
- **FR-016**: The interface MUST reflect the status change (`processing → ready/failed`) **without a full
  page reload**, via a **server push (SSE) channel**: a client subscribes and receives the document's
  `ready`/`failed` update as it happens (scoped to its own session). A failed document MUST show its
  reason and offer a delete action. (Polling is not used.)

### Key Entities *(include if feature involves data)*

- **Chunk**: A slice of a document's text with its position (index), optional source page number, and its
  embedding vector. Belongs to exactly one document, cascade-deleted with it, unique per
  `(document, index)`. The unit of retrieval for the RAG stories.
- **Embedding vector**: The numeric representation of a chunk's text produced by the centralized model,
  of a fixed configured dimension; comparable across the index because one model is used throughout.
- **Document (status/lifecycle)**: The US-04 document, whose lifecycle this story drives —
  `Processing → Ready` (with chunk count) or `Processing → Failed` (with a reason). Extends the aggregate
  with the ready/failed transitions.
- **Processing job**: The durable background unit of work for one document, triggered by the "uploaded"
  signal; idempotent and retryable.
- **Chunking / embedding configuration**: The config-driven parameters (chunk size, overlap, batch size,
  model + dimension, retry count) that govern the pipeline.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A readable document uploaded to the session is indexed end-to-end (chunks + vectors stored,
  document *ready*, chunk count set) in 100% of readable uploads, with the tree reflecting *ready*
  without a page reload.
- **SC-002**: 100% of unreadable files (no extractable text) end *failed* with a human-readable reason
  and **zero** chunks stored.
- **SC-003**: Under a permanent provider error, the worker retries the configured number of times and
  then marks the document *failed* with a provider-error reason; **zero** partial chunks remain (0
  orphans across repeated runs).
- **SC-004**: Redelivering the processing signal for a document yields **zero** duplicate chunks (the
  final chunk set is identical to a single run) in 100% of cases.
- **SC-005**: For a document producing N chunks with batch size B, the provider is called ⌈N ÷ B⌉ times
  (e.g. 200 chunks / 64 → 4 calls), not N times.
- **SC-006**: After a restart with a document still queued, that document is processed to completion (not
  lost) in 100% of cases.
- **SC-007**: A document's chunks are visible to, or usable by, another session in **0%** of cases.
- **SC-008**: A processed document's chunks all carry the same vector dimension (the configured model's),
  so the whole index is query-comparable.

## Assumptions

- **US-04 and US-07 are in place**: the upload + `DocumentUploaded` event, the binary in storage
  (`IFileStorage`), and the document's `Status`/`ChunkCount`/`FailureReason` fields already exist and are
  reused; US-06 adds the worker, the extractor/chunker/embedding seams, the chunk store, and the
  ready/failed transitions.
- **Embeddings are centralized** (application key, not BYOK); one model (`voyage-3.5`, dim `1024`) for the
  whole index. The provider is behind an abstraction with **a real Voyage driver and a deterministic
  stand-in** (used in dev/tests without a key); a different or local model is a future swap (out of scope)
  and a model change implies a full re-index.
- **The vector store supports similarity search** with the configured dimension (`1024`).
- **The UI learns of status changes via a server push (SSE) channel** without a reload — the client
  subscribes and receives `ready`/`failed` updates for its own session as they happen.
- **Out of scope**: OCR of scans, a local/self-hosted embedding model (the abstraction allows it),
  on-demand re-indexing, and the question-answering/RAG + streaming stories (US-13/14/15).
- **Single-file scale**: documents are bounded by the upload quota (≤ the per-file limit); no
  large-corpus/scale tuning is required for the case study.
