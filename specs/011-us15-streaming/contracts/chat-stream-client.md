# Contract — chat stream client + backend hardening (US-15)

US-15 adds **no new HTTP contract** — it consumes US-14's `POST /api/chat/ask` SSE. This documents the client's handling and the two backend hardening touches.

## Client consumption of `POST /api/chat/ask`

Request (US-14): `{ "question": "...", "scope": { "type": "all|folder|document", "targetId": "…?" } }`.

The client opens a **streaming fetch** with an `AbortController` signal and reads `response.body` incrementally:

1. **Non-2xx** (pre-stream ProblemDetails) → read `.code` → mark the exchange `error` with the mapped PL message (+ Try again).
2. **2xx `text/event-stream`** → parse events as they arrive:
   - `sources` → set the exchange's source list.
   - `token` → **append** `data.text` to the answer (incremental render).
   - `done` → `complete`; if `groundsFound === false`, show the neutral no-basis note (no answer text).
   - `error` → `error` + mapped message + Try again.
   - `:` comment (heartbeat) → ignored.
3. **Stream ends without `done`** → treat as `error` ("stream interrupted") — never a clean answer.
4. **`stop()` / a new ask** → `AbortController.abort()`; the in-flight exchange becomes `interrupted` with its partial answer kept; the server (US-14) cancels generation on the disconnect.

Event **order** is US-14's real order: `sources` → `token`* → `done` (the source list renders once `sources` arrives).

## Backend hardening (in the existing endpoint)

- **Keep-alive**: while streaming, emit an SSE comment (`: keep-alive\n\n`) every `Rag:StreamHeartbeatSeconds` (default 15) until completion; all response writes are serialized (a `SemaphoreSlim`) so heartbeat + events never interleave. Headers stay `Cache-Control: no-cache`, unbuffered.
- **Cancellation** (already wired in US-14, **verified** here): when the client aborts, the request `CancellationToken` cancels `IAnswerGenerator.GenerateAsync` — no hanging provider call. An integration test asserts the (extended) fake generator observed cancellation.

## Guarantees (asserted by tests)

- The answer updates **multiple times** during a stream (incremental), first update before `done`.
- `stop()` halts appending, marks `interrupted`, keeps the partial answer, and the backend generation is cancelled.
- A mid-stream `error` or a missing `done` yields a visible message + Try again.
- The scope selector offers All / folder / **ready** document; the chosen scope is sent with the question.
- Exactly one stream is active at a time.
- A heartbeat comment is emitted at least once per configured interval on a slow generation.
