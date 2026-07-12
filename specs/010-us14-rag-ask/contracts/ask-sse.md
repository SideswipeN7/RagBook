# Contract — POST /api/chat/ask (streaming) + IAnswerGenerator seam (US-14)

## `POST /api/chat/ask`

Ask a scoped question; the grounded answer streams back as Server-Sent Events. Session by the `ragbook_session` cookie (US-01). The question is in the **body** (never the URL).

### Request body
```json
{
  "question": "Jaki jest okres wypowiedzenia?",
  "scope": { "type": "folder", "targetId": "…guid…" }
}
```
`scope.type` ∈ `all` | `folder` | `document`; `targetId` required for `folder`/`document`, absent for `all`.

### Pre-stream failures → RFC 9457 ProblemDetails (normal JSON, no stream)

| Status | code | When |
|---|---|---|
| 400 | `chat.invalid_question` | empty/whitespace or > 2000 chars |
| 401 | `settings.api_key_missing` | session has no active key (guard, before any provider call) |
| 404 | `chat.scope_not_found` | folder/document scope target not visible to the session |
| 400 | `settings.invalid_api_key` | provider rejected the key **before** the first delta |
| 429 | `chat.provider_rate_limited` | provider throttled **before** the first delta |
| 503 | `chat.provider_unavailable` | provider server/timeout **before** the first delta |

### Success / mid-stream → `200 text/event-stream`

Emitted in order:

```
event: sources
data: [{"number":1,"documentId":"…","fileName":"umowa.pdf","pageNumber":3}, …]

event: token
data: {"text":"Okres wypowiedzenia"}

event: token
data: {"text":" wynosi 3 miesiące [1]."}

event: done
data: {"groundsFound":true}
```

- **Insufficient grounding** (empty scope or all matches below `SimilarityThreshold`): no provider call — a single `done` with `groundsFound:false` (US-17 renders the refusal); `sources` is empty/omitted.
- **Provider failure after streaming began**: an `error` event, then the stream closes:
  ```
  event: error
  data: {"code":"chat.provider_unavailable"}
  ```
- Headers: `Content-Type: text/event-stream`, `Cache-Control: no-cache`.

### Guarantees (asserted by tests)
- Only in-scope, current-session, ready passages appear in `sources`/prompt (US-13 isolation).
- Below-threshold-only or empty scope ⇒ **0** provider calls ⇒ `groundsFound:false`.
- Answer is emitted incrementally (multiple `token` events), not one block.
- Every provider failure mode surfaces its distinct code (pre-stream ProblemDetails or mid-stream `error`).

## Internal seam — `IAnswerGenerator` (US-15 also consumes)

```
IAsyncEnumerable<string> GenerateAsync(GroundedContext context, CancellationToken ct)
```
- Yields answer text **deltas** as the provider produces them.
- Throws `AnswerGenerationException(AnswerGenerationFailure)` — `InvalidKey` / `RateLimited` / `Unavailable` — mapping the provider's `401/403` / `429` / `5xx·timeout·overloaded`. Tests replace it with a deterministic streaming fake (no real Anthropic).
