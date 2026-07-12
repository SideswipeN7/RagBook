# Phase 0 — Research & Decisions: US-15 streaming chat UI + hardening

Grounded in the merged US-14 (`POST /api/chat/ask` SSE: `sources`/`token`/`done{groundsFound}`/`error{code}`, request `CancellationToken` → generator), US-02 (`ApiKeyStore.chatLocked`), US-07 (`TreeStore` folders+documents), and the repo's existing SSE consumer (US-06 `DocumentStatusStore` uses `EventSource` — GET only).

## D1 — Consume the stream with streaming `fetch`, not `HttpClient`/`EventSource`

- **Decision**: `ChatStore.ask` uses the **Fetch API**: `fetch('/api/chat/ask', { method:'POST', body, signal })`, then reads `response.body!.getReader()`, decodes with `TextDecoder`, and feeds bytes to a small **pure `sse-parser`** that emits `{event, data}` records as they complete (buffering across chunk boundaries, splitting on `\n\n`). Angular `HttpClient` does not surface a token stream, and `EventSource` cannot issue a POST (and the question must be a body, not a URL — privacy). Abort via `AbortController.signal`.
- **Rationale**: This is the only way to consume a POST token stream incrementally in the browser; isolating the SSE parsing in a pure function makes it unit-testable without a network.
- **Alternatives rejected**: `EventSource` (GET-only, question in URL); `HttpClient` (buffers the response, no incremental deltas); WebSocket (out of scope).

## D2 — `ChatStore` thread + states (one active generation)

- **Decision**: A signal `thread` = ordered list of **exchanges** `{ id, question, scope, status: 'streaming'|'complete'|'interrupted'|'error', answer: string, sources: Source[], errorMessage?: string }`. `ask(question, scope)`: **abort any active stream first** (FR-008), push a new `streaming` exchange, open the fetch stream, and per event: `sources` → set `sources`; `token` → **append** `data.text` to `answer` (a new signal value each token → incremental render); `done` → `complete` (or a `no-basis` marker when `groundsFound:false`); `error` → `error` + mapped message. Stream end **without** a `done` → treat as `error` (FR-005). The `AbortController` is held on the store; `stop()` aborts it → the exchange becomes `interrupted` (partial `answer` kept).
- **Rationale**: Matches the multi-turn clarify decision; the append-per-token via a fresh signal value drives OnPush incremental rendering (SC-001). One controller enforces one active stream.

## D3 — Stop/abort → server cancellation (already wired)

- **Decision**: `stop()` (and asking again) calls `AbortController.abort()`, which cancels the `fetch` and closes the connection. The server's request `CancellationToken` (already flowed to `IAnswerGenerator` in US-14) cancels generation — **no backend change needed for cancellation**. US-15 adds an **integration test** proving it: abort the client mid-stream, assert the (extended) fake generator observed cancellation.
- **Rationale**: US-14 already threads the token; US-15 verifies the end-to-end abort→cancel path (FR-004, AC-2/AC-5) rather than re-implementing it.

## D4 — Error mapping (PL) + incomplete-stream detection

- **Decision**: A `Record<string,string>` maps the stable codes to PL messages: `settings.api_key_missing`, `settings.invalid_api_key`, `chat.provider_rate_limited`, `chat.provider_unavailable`, `chat.scope_not_found`, `chat.invalid_question` → readable text; unknown → a generic message. Pre-stream failures arrive as a **ProblemDetails** JSON (non-2xx `fetch` response) — read `.code`; mid-stream failures arrive as an `error` SSE event — read `data.code`. A reader that ends before a `done` sets a generic "stream interrupted" error. Each `error` exchange offers **Try again** (re-run `ask` with the same question+scope).
- **Rationale**: FR-005 + AC-3 — a failure is always explained with a retry, never a silent truncation; consistent with the existing store code→message pattern.

## D5 — Scope selector from `TreeStore`

- **Decision**: A `scope-selector` derives options from `TreeStore`: **All documents** (default), each **folder** (by id, includes its subtree — US-13), and each **ready** document (by id; `status === 'Ready'` only — processing/failed not selectable). The selected scope is a signal `{ type:'all'|'folder'|'document', targetId? }` shown as an **active chip** above the input and sent in the `ask` body. Labels come from the tree (folder name / file name).
- **Rationale**: Reuses the single `GET /api/tree` source (no new endpoint); FR-006 restricts file scope to ready documents (retrieval only sees ready anyway).

## D6 — Backend keep-alive (heartbeat)

- **Decision**: During `StreamAnswerAsync`, run a background loop that writes an SSE **comment** (`: keep-alive\n\n`) every `RagOptions.StreamHeartbeatSeconds` (default 15) until the answer completes; **all** `Response` writes (events + heartbeat) go through a `SemaphoreSlim` so the two producers never interleave a write. The loop is cancelled in a `finally`. Streaming headers stay `Cache-Control: no-cache` (already set); response buffering is not enabled for this endpoint.
- **Rationale**: FR-010 — a long generation behind a proxy (Cloud Run) must not idle-timeout; a comment is ignored by the SSE parser. The semaphore avoids interleaved writes (the token loop and the heartbeat both write to one response).
- **Testing**: set a tiny `StreamHeartbeatSeconds` via the test host + a fake generator that delays between deltas → assert a comment line appears in the body before `done`.

## D7 — Testing the fetch stream

- **Decision**: `ChatStore`/`sse-parser` tests **stub the global `fetch`** (`spyOn(window, 'fetch')`) to return a `Response` whose `body` is a scripted `ReadableStream` of SSE bytes (built from `TextEncoder`), and drive: incremental `answer` growth, `sources` set, `done`/no-basis, `error` event, stream-end-without-`done`, and abort→`interrupted`. The pure `sse-parser` is tested directly with chunk boundaries splitting an event. No `HttpTestingController` (it does not intercept `fetch`).
- **Rationale**: Deterministic, offline; exercises the real parsing + state transitions.

## Open items deferred (not blocking)

- Clickable `[n]` citations / navigation → **US-16** (US-15 renders sources as a plain list).
- Full refusal detection/UX + sentinel → **US-17** (US-15 shows a neutral no-basis note on `groundsFound:false`).
- Conversation persistence across reloads → **US-18**.
- Stream resumption / WebSocket → out of scope.
