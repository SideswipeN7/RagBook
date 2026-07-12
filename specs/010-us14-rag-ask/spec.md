# Feature Specification: Zadanie pytania z RAG — streaming backend (US-14)

**Feature Branch**: `010-us14-rag-ask`

**Created**: 2026-07-12

**Status**: Draft

**Input**: User description: "US-14 — Pytanie z RAG. Backend pipeline: walidacja → retrieval (US-13) → próg podobieństwa → prompt grounding → strumieniowa generacja Claude (SSE). Streaming od razu; UI czatu w US-15; cytaty w US-16; brak-podstaw UX w US-17; persystencja w US-18."

## Boundary note (US-14 vs US-15/16/17/18)

US-14 delivers the **streaming answer backend** — the pipeline that turns a scoped question into a grounded answer **streamed incrementally** to the caller. It builds the streaming transport now (a settled decision), but the **chat UI** (question field + rendering the stream) is **US-15**, **clickable citations** ([n]→document) are **US-16**, the user-facing **"no basis" refusal** rendering/detection is **US-17**, and **conversation persistence** (saving the question/answer/used chunks) is **US-18**. Here the ask is **stateless** — the scope is supplied per request. The acceptance criteria are expressed against this backend, testable with a mocked generator (no test hits a real provider).

## Clarifications

### Session 2026-07-12

- Q: How is the answer stream transported (shapes the endpoint + the US-15 client contract)? → A: **`POST /api/chat/ask`** with the question + scope in the JSON **body**, responding `text/event-stream` (consumed via fetch + ReadableStream in US-15). Typed events: **`sources`** (the numbered passages, first), **`token`** (answer deltas), **`done`** (completion), **`error`** (a distinct code). The question is **never** placed in the URL (it may be sensitive).
- Q: Generation error codes (AC-5) — reuse US-02's `settings.*` or new `chat.*`? → A: **Reuse the key codes** — a rejected/invalid key is `settings.invalid_api_key` (400) and a missing key is `settings.api_key_missing` (401) (same meaning as US-02). **Generation-time provider errors get new `chat.*` codes**: `chat.provider_rate_limited` (429) and `chat.provider_unavailable` (503).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Grounded answer, streamed (Priority: P1)

A user asks a natural-language question within a scope that contains ready documents holding the answer, and receives an answer **built only from those documents' passages**, streamed incrementally, with claims marked by source numbers `[n]` that map to the passages the model was given.

**Why this priority**: This is the feature — the first end-to-end RAG answer. Streaming is the settled delivery mode. Everything else guards or configures this path.

**Independent Test**: With ready documents indexed in a scope, ask a question they answer (mocked generator echoing the grounded context); the response streams incrementally, the answer draws on the passages, and the passages it was given are conveyed so `[n]` references resolve.

**Acceptance Scenarios**:

1. **Given** ready documents in a scope that contain the answer, **When** the user asks a question in that scope, **Then** the answer is generated **only** from the retrieved passages and is delivered as an **incremental stream** (not one block).
2. **Given** the same, **When** the answer streams, **Then** the passages supplied to the model (each numbered `[n]` with its document + page) are conveyed to the caller so `[n]` references can later resolve to sources (US-16).
3. **Given** a question in a language, **When** the answer is generated, **Then** it is produced in the **question's language** (grounding-prompt instruction).

---

### User Story 2 - Retrieval respects scope and session (Priority: P1)

Passages from documents outside the chosen scope, or from another session, are **never** placed into the prompt — even when they contain similar text.

**Why this priority**: The grounding guarantee is only as good as the retrieval boundary; a leak here silently poisons answers with out-of-scope content.

**Independent Test**: Seed similar content inside and outside the scope (and in another session); ask in the chosen scope; assert the prompt's passages all come from in-scope, current-session documents.

**Acceptance Scenarios**:

1. **Given** documents inside and outside the scope with similar content, **When** the user asks within the scope, **Then** no passage from outside the scope reaches the prompt.
2. **Given** another session's documents with matching content, **When** the user asks, **Then** none of that session's passages reach the prompt (isolation, reusing US-13).

---

### User Story 3 - Insufficient grounding short-circuits generation (Priority: P1)

When retrieval finds nothing relevant enough (all passages below the similarity threshold, or the scope is empty), the model is **not called** — the pipeline returns a deterministic **"insufficient grounding"** outcome instead of inviting a hallucinated answer.

**Why this priority**: Prevents the model from answering without grounds (the core "no hallucination" promise). Deterministic and cheap; the user-facing refusal presentation is US-17.

**Independent Test**: Ask a question unrelated to any indexed content (all matches below threshold); assert the generator is never invoked and the outcome is the distinct "insufficient grounding" signal.

**Acceptance Scenarios**:

1. **Given** a question whose retrieval yields only below-threshold matches, **When** the pipeline runs, **Then** the model is **not** invoked and the result is the "insufficient grounding" outcome.
2. **Given** a scope whose documents are all still processing (no ready content), **When** the user asks, **Then** the empty-scope path is taken (no model call), consistent with US-13.

---

### User Story 4 - Tunable without code changes (Priority: P1)

The retrieval breadth (`TopK`), the relevance cutoff (`SimilarityThreshold`), and the context budget (`MaxContextChars`) are configuration — changing them changes behavior without a code change.

**Why this priority**: RAG quality is tuned empirically; the constitution mandates config-driven parameters (no magic numbers).

**Independent Test**: Run the pipeline with two different threshold/TopK/MaxContextChars configurations and observe the eligible-passage set and context size change accordingly, with no code edit.

**Acceptance Scenarios**:

1. **Given** a changed `SimilarityThreshold`, **When** the pipeline runs, **Then** the set of passages that pass the cutoff reflects the new value.
2. **Given** a changed `MaxContextChars`, **When** the assembled context would exceed it, **Then** the **weakest** (least relevant) passages are dropped first until it fits.
3. **Given** a changed `TopK`, **When** retrieval runs, **Then** at most that many passages are considered.

---

### User Story 5 - Provider failures are distinguishable (Priority: P1)

When the generation provider fails — an invalid/expired key, rate limiting, or a server/timeout error — the caller receives a **distinct, stable error signal** (not a generic failure), whether the failure happens before the first token or partway through the stream.

**Why this priority**: The frontend (US-15/19) must show the right message ("check your key" vs "try again shortly" vs "service unavailable"); an opaque failure blocks that.

**Independent Test**: With a mocked generator configured to fail with each mode (rejected key, rate-limited, unavailable), before and mid-stream, assert each surfaces its distinct stable code.

**Acceptance Scenarios**:

1. **Given** no active key for the session (BYOK, non-demo), **When** the user asks, **Then** generation is refused **before** any provider call with the `settings.api_key_missing` signal (reusing US-02).
2. **Given** the provider rejects the key, is rate-limited, or is unavailable **before** the first token, **When** the pipeline runs, **Then** the result is a failure with a distinct code (invalid key / rate limited / provider unavailable).
3. **Given** the provider fails **partway through** the stream, **When** streaming, **Then** an **error signal with the distinct code** is emitted on the stream (the partial answer is not silently truncated as if complete).

---

### Edge Cases

- **Empty or over-long question** (empty/whitespace, or > 2000 characters) → rejected as `chat.invalid_question` (400) before any retrieval or generation.
- **All documents in scope still processing** → empty-scope path (US-13 AC-5): no model call, the empty/insufficient outcome.
- **Very long passages** exceeding the context budget → trimmed by dropping the **least relevant** passages first (`MaxContextChars`).
- **Key expires mid-session** before generation → `settings.api_key_missing` before the provider call.
- **Provider error mid-stream** → a distinct error event on the stream, not a clean completion.
- **Scope target deleted** (folder/document) → `chat.scope_not_found` (reusing US-13), before generation.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST accept a natural-language question plus a scope and produce an answer grounded **only** in the retrieved passages of that scope.
- **FR-002**: The system MUST validate the question and reject an empty/whitespace one or one longer than the configured maximum (2000 characters) as `chat.invalid_question` (400), before retrieval or generation.
- **FR-003**: The system MUST require an active generation credential for the session before calling the provider; with none (BYOK, non-demo) it MUST fail `settings.api_key_missing` before any provider call (reusing US-02's guard).
- **FR-004**: The system MUST retrieve candidate passages through the existing scoped-retrieval capability (US-13) — session-scoped, ready-only, scope-filtered — and MUST NOT introduce a second retrieval path. A not-visible scope target surfaces as `chat.scope_not_found`; an empty scope takes the no-generation path.
- **FR-005**: The system MUST discard retrieved passages below the configured **similarity threshold**; if none remain (or the scope was empty), it MUST return the deterministic **"insufficient grounding"** outcome **without** calling the provider.
- **FR-006**: The system MUST assemble the grounding context from the surviving passages, **numbered `[1]..[K]`** with each passage's source document and page, ordered most-relevant first, and MUST trim it to the configured **maximum context size** by dropping the **least relevant** passages first.
- **FR-007**: The grounding prompt MUST instruct the model to: answer **only** from the provided passages; mark claims with their source number `[n]`; if the passages do not contain the answer, reply with a **fixed refusal phrase** (the contract US-17 consumes); and answer in the **question's language**. The prompt MUST be a maintained, commented artifact (not scattered string literals).
- **FR-008**: The system MUST **stream** the answer over **`POST /api/chat/ask`** (question + scope in the JSON body) as **`text/event-stream`**, emitting the content **incrementally** (not a single completed block) via typed events: a first **`sources`** event, repeated **`token`** events, a terminal **`done`** event, and an **`error`** event on failure. The question MUST NOT be carried in the URL.
- **FR-009**: The system MUST convey to the caller the passages it grounded on — each with its `[n]` number, source document, and page — as the initial **`sources`** event, so `[n]` references in the answer can resolve to sources (consumed by US-16).
- **FR-010**: On a provider failure the system MUST surface a **distinct, stable error code**: a rejected/invalid key → `settings.invalid_api_key`, a missing key → `settings.api_key_missing` (both reused from US-02), provider rate-limiting → `chat.provider_rate_limited` (429), and a provider server/timeout error → `chat.provider_unavailable` (503) — both when the failure precedes the first token (a failed result → ProblemDetails) and when it occurs mid-stream (an `error` event carrying the code).
- **FR-011**: The retrieval breadth (`TopK`), similarity threshold, and maximum context size MUST be **configuration-driven** — changing them changes behavior with no code change.
- **FR-012**: No automated test may call the real generation provider — generation MUST sit behind a seam that tests replace with a deterministic streaming fake (constitution §V).

### Key Entities *(include if feature involves data)*

- **Question**: the user's natural-language query text (validated: non-empty, ≤ 2000 chars) plus the scope it is asked in.
- **Grounding passage**: a retrieved chunk that survived the threshold — its source number `[n]`, document (id + file name), page, and text — used to build the prompt and (via US-16) the citations.
- **Answer stream**: the incremental sequence delivered to the caller — the grounding passages (so `[n]` resolves), the answer content as it is produced, a completion signal, and — on failure — a distinct error signal.
- **RagOptions** (extended from US-13): `TopK`, **`SimilarityThreshold`**, **`MaxContextChars`** — all configuration.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: For a question answered by in-scope documents, the answer is grounded in the retrieved passages and the caller receives the passages it was grounded on for `[n]` resolution — in **100%** of grounded answers.
- **SC-002**: **0** passages from outside the scope or from another session ever reach the prompt.
- **SC-003**: A question with only below-threshold matches results in **0** provider calls and the deterministic "insufficient grounding" outcome, every time.
- **SC-004**: The answer is delivered incrementally — the caller receives the first content **before** the answer is complete (streaming, not buffered) — in **100%** of successful answers.
- **SC-005**: Changing `SimilarityThreshold`, `TopK`, or `MaxContextChars` in configuration changes the pipeline's behavior with **no** code change.
- **SC-006**: Each provider failure mode (invalid key / rate-limited / unavailable), before or during the stream, surfaces its **distinct** stable code — **100%** of the time, never a generic/opaque failure.
- **SC-007**: An empty/over-long question is rejected before any retrieval or generation, **100%** of the time.

## Assumptions

- **Streaming transport (decided — see Clarifications)**: `POST /api/chat/ask` (question + scope in the JSON body) responds `text/event-stream` with typed events — `sources` (numbered passages, first), `token` (answer deltas), `done` (completion), `error` (distinct code on failure). The question is never in the URL (privacy).
- **Similarity vs distance**: retrieval (US-13) returns cosine **distance**; the threshold is expressed as a **similarity** (`SimilarityThreshold = 0.75` ⇒ keep passages with cosine similarity ≥ 0.75, i.e. distance ≤ 0.25). Applied to US-13's returned matches (post-retrieval), since US-13 caps by `TopK` distance-first.
- **Generation credential**: the BYOK key via US-02's `IAnthropicClientFactory` (guard → `settings.api_key_missing`). Demo-mode generation (US-03) is mentioned as a future alternative credential path but is **not built here**.
- **Real generation client**: US-02 deferred the streaming generation client to US-14; it is introduced here behind the generation seam. Tests use a deterministic streaming fake — no test hits Anthropic.
- **Stateless ask**: the scope is supplied per request; persisting the question/answer/used passages on a conversation is **US-18**.
- **Refusal handling**: US-14 emits the grounding prompt (with the fixed refusal phrase) and the "insufficient grounding" short-circuit; **detecting/rendering** the refusal to the user is **US-17**.
- One embedding model governs the whole index (US-06); the question is embedded with the same model (inside US-13's retriever).

## Out of Scope

- Chat UI: the question field and rendering the streamed answer (**US-15**).
- Clickable citations and `[n]`→document mapping in the UI (**US-16**).
- User-facing "no basis" refusal detection/rendering (**US-17**).
- Conversation persistence — saving questions, answers, and used passages (**US-18**).
- Full demo-mode generation credential (**US-03**).
- Re-ranking (cross-encoder), hybrid BM25+vector search, and query rewriting — deliberate MVP simplifications (noted in README).
