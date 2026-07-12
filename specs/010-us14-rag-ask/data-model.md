# Phase 1 — Data Model: US-14 streaming RAG ask

**No persisted (database) entities and no migration.** The ask is stateless (persistence is US-18). This documents the value objects, the seams, the streamed events, config, and the error catalog.

## Value objects (Chat/Domain)

### GroundingPassage

A retrieved chunk that survived the threshold, numbered for the prompt + citations.

| Field | Type | Source |
|---|---|---|
| `Number` | int | 1-based `[n]` (most-relevant first) |
| `DocumentId` | Guid | from the retrieved chunk |
| `FileName` | string | source document |
| `PageNumber` | int? | source page (null for TXT/MD) |
| `Text` | string | the passage text |

### GroundedContext

| Field | Type | Notes |
|---|---|---|
| `Sources` | IReadOnlyList<GroundingPassage> | the numbered passages, most-relevant first |
| `Prompt` | string | the assembled user message (numbered passages + question), trimmed to `MaxContextChars` |

### AskOutcome (discriminated)

- `Answerable(GroundedContext context)` — grounds found; stream the answer.
- `InsufficientGrounding` — empty scope or all matches below threshold; **no** provider call.

### AnswerGenerationFailure (enum) / AnswerGenerationException

- `InvalidKey` → `settings.invalid_api_key` (400)
- `RateLimited` → `chat.provider_rate_limited` (429)
- `Unavailable` → `chat.provider_unavailable` (503)

`AnswerGenerationException` carries the failure kind; thrown by `IAnswerGenerator` (before the first delta → ProblemDetails; after → SSE `error`).

## Seams

- `IPromptBuilder.Build(string question, IReadOnlyList<RetrievedChunk> passages) → GroundedContext` — pure: number, order, trim to `MaxContextChars` (weakest-first).
- `IAnswerGenerator.GenerateAsync(GroundedContext context, CancellationToken) → IAsyncEnumerable<string>` — streams answer deltas; throws `AnswerGenerationException`.
- `IAskQuestionPipeline.PrepareAsync(string question, ChatScope scope, CancellationToken) → Task<Result<AskOutcome>>` — validate → retrieve (US-13) → threshold → build. (The endpoint runs the key guard and the streaming separately.)

## Streamed events (`text/event-stream`)

| Event | Payload (JSON) | When |
|---|---|---|
| `sources` | `[{ number, documentId, fileName, pageNumber }]` | first, for an answerable question (so `[n]` resolves — US-16) |
| `token` | `{ text }` | each answer delta |
| `done` | `{ groundsFound: bool }` | completion — `false` for insufficient grounding (no tokens), `true` after an answer |
| `error` | `{ code }` | a provider failure that occurred **after** streaming began |

## Configuration

`RagOptions` (`Rag` section, from US-13) — **extended**:

| Property | Type | Default | Meaning |
|---|---|---|---|
| `TopK` | int | 8 | retrieval breadth (US-13) |
| `SimilarityThreshold` | double | 0.75 | keep passages with cosine similarity ≥ this (distance ≤ 1 − this) |
| `MaxContextChars` | int | 8000 | maximum assembled context size; weakest passages dropped first |

`AnthropicOptions` (`Anthropic` section, from US-02) — **extended**:

| Property | Type | Default | Meaning |
|---|---|---|---|
| `GenerationModel` | string | `claude-sonnet-5` | the Claude model for generation |
| `MaxOutputTokens` | int | 1024 | max tokens per answer |

## Error catalog

| Code | ErrorType | HTTP | Trigger |
|---|---|---|---|
| `chat.invalid_question` | Validation | 400 | empty/whitespace or > 2000 chars |
| `settings.api_key_missing` | Unauthorized | 401 | no active key (reused from US-02; pre-generation guard) |
| `settings.invalid_api_key` | Validation | 400 | provider rejected the key during generation (reused from US-02) |
| `chat.provider_rate_limited` | RateLimited | 429 | provider throttled generation (NEW) |
| `chat.provider_unavailable` | Unavailable | 503 | provider server/timeout error during generation (NEW) |
| `chat.scope_not_found` | NotFound | 404 | scope target not visible (reused from US-13) |

`RateLimited` (429) and `Unavailable` (503) `ErrorType`s already exist (added in US-02). No shared-kernel change.

## Invariants

- The question is validated before any retrieval/generation; never carried in the URL.
- Only in-scope, current-session, ready passages ever reach the prompt (US-13).
- No passage is placed in the prompt unless its similarity ≥ `SimilarityThreshold`; if none qualify → no provider call.
- The assembled context never exceeds `MaxContextChars`; trimming drops whole weakest passages.
- The `sources` event's numbering matches the `[n]` the prompt uses.
- A provider failure never surfaces as a clean `done` — it is a `ProblemDetails` (pre-stream) or an `error` event (mid-stream).
