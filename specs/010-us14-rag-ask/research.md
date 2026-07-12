# Phase 0 — Research & Decisions: US-14 streaming RAG ask

Grounded in the merged US-13 (`IScopedRetriever` → `RetrievedChunk{…, Distance}`), US-02 (`IAnthropicClientFactory` → `settings.api_key_missing`; thin resilient validation `HttpClient`), US-06 (`IEmbeddingProvider`), and the repo's SSE precedent (US-06 `DocumentStatusEndpoints`, manual `Response.WriteAsync`).

## D1 — Similarity threshold (post-retrieval, on US-13 matches)

- **Decision**: US-13 returns the `TopK` nearest matches with **cosine distance**. US-14 keeps a match iff `similarity ≥ SimilarityThreshold`, where `similarity = 1 − distance` (HNSW `vector_cosine_ops`). So the cutoff is `distance ≤ 1 − SimilarityThreshold` (threshold 0.75 ⇒ distance ≤ 0.25). If none survive (or `IScopedRetriever` reported `IsEmptyScope`) → `AskOutcome.InsufficientGrounding` and the provider is **not** called.
- **Rationale**: Reuses US-13 unchanged (it deliberately deferred the threshold). Applying it on the returned `TopK` is deterministic and cheap; the "no grounds → don't call the model" behavior is the core anti-hallucination guard.
- **Alternatives rejected**: pushing the threshold into the US-13 SQL (would re-open US-13's fixed seam); calling the model with empty context (invites a hallucinated answer — the opposite of the goal).

## D2 — Streaming generation seam + real client

- **Decision**: `IAnswerGenerator.GenerateAsync(GroundedContext, ct) : IAsyncEnumerable<string>` yields answer text deltas; on failure it throws `AnswerGenerationException(AnswerGenerationFailure)` (`InvalidKey` / `RateLimited` / `Unavailable`). Real impl `AnthropicAnswerGenerator` (Infrastructure) resolves the session key via `IAnthropicClientFactory`, then `POST {BaseUrl}/v1/messages` with `"stream": true` on a **named resilient `HttpClient`** (mirrors US-02's validation client; `AddStandardResilienceHandler`), and parses the response SSE: `content_block_delta` → `delta.text` (yield), `message_stop` → end, an `error` event or non-2xx status → throw the mapped failure. Status mapping: `401/403` → InvalidKey, `429` → RateLimited, `5xx` / overloaded / network / timeout → Unavailable.
- **Rationale**: Keeps generation behind a fakeable seam (§V) so no test hits Anthropic; a thin `HttpClient` avoids pinning an SDK (consistent with US-02) and gives full control of the streaming parse. The `IAsyncEnumerable<string>` is the natural streaming shape the endpoint pipes to SSE.
- **Streaming resilience caveat (C1)**: the generation client MUST NOT reuse US-02's `AddStandardResilienceHandler` verbatim — its **total-request-timeout** (≈30s) would truncate a long answer stream and its **retry** would wrongly re-issue a partially-consumed POST. For streaming, read with `HttpCompletionOption.ResponseHeadersRead`, set `HttpClient.Timeout = InfiniteTimeSpan`, and either omit the standard handler (keep only a short **connect/attempt** timeout for establishing the request) or disable `TotalRequestTimeout` + set `Retry.MaxRetryAttempts = 0`. Cancellation flows via the request `CancellationToken` (client disconnect).
- **Testing the real client**: a canned `HttpMessageHandler` returns a fixed Anthropic-style SSE body (and each error status) → asserts deltas parsed in order and each failure maps to the right `AnswerGenerationFailure` — deterministic, offline (US-02 validator-test pattern).
- **Model/tokens**: `AnthropicOptions` grows `GenerationModel` (default a current Claude, e.g. `claude-sonnet-5`) and `MaxOutputTokens` — config-driven (no magic numbers); changing the model is a config edit.

## D3 — SSE transport + the pre-first-delta boundary

- **Decision**: `POST /api/chat/ask` reads `{ question, scope }`. The endpoint writes SSE **manually** (`Response.Headers.ContentType = "text/event-stream"`, then `event: <type>\ndata: <json>\n\n` + `Body.FlushAsync`) — the US-06 status-stream pattern — with events `sources`, `token`, `done`, `error`. To honour FR-010's "before the first token → ProblemDetails vs mid-stream → error event": the endpoint **peeks the first delta** from the generator (`await enumerator.MoveNextAsync()`) **before** writing any response bytes. If that throws → the response is still unwritten → return a normal `ProblemDetails` with the mapped code. If it yields → write the `sources` event, then the buffered first delta and the rest as `token` events, then `done`. A failure on a **later** `MoveNextAsync` (headers already sent) → an `error` event carrying the code, then close.
- **Rationale**: A token stream is not a single `Result`, so it is driven from the endpoint (already precedented by US-06). Peeking the first delta cleanly separates "couldn't start" (ProblemDetails) from "failed mid-answer" (SSE error) without buffering the whole answer.
- **Privacy**: the question is in the POST **body**, never the URL (clarify decision).
- **Alternatives rejected**: `GET ?question=…` + `EventSource` (question in URL — sensitive-data leak); buffering the whole answer to return via Wolverine (defeats streaming).

## D4 — Grounding prompt (commented contract)

- **Decision**: `GroundingPrompt` — a maintained constant (with an explaining comment) holding the system instruction: answer **only** from the numbered passages; mark each claim with its source number `[n]`; if the passages do not contain the answer, reply with the **exact refusal phrase** (a named constant — the contract US-17 detects); answer in the **question's language**. `PromptBuilder` composes the user message: the numbered passages (`[1] (file, p.X): …`) followed by the question.
- **Rationale**: FR-007 requires a single maintained prompt artifact (not scattered literals); the refusal phrase must be a shared constant so US-17 can match it exactly.

## D5 — Context assembly & trimming

- **Decision**: Surviving passages are ordered most-relevant-first (ascending distance = descending similarity), numbered `[1..K]`, and concatenated. If the assembled context would exceed `MaxContextChars`, drop whole passages from the **weakest** end until it fits (never split a passage mid-way). `MaxContextChars` default ≈ 8000.
- **Rationale**: FR-006 — keep the strongest evidence within a bounded prompt; dropping weakest-first preserves the best grounding. Whole-passage dropping keeps `[n]`↔passage mapping intact for citations.

## D6 — Key guard placement

- **Decision**: The **API endpoint** performs the pre-generation key guard via `IAnthropicClientFactory.CreateForSession()` → on failure returns `ProblemDetails` (`settings.api_key_missing`, 401) before touching the pipeline/stream. The generator (Infra) also resolves the key when it actually calls the provider. The Core `AskQuestionPipeline` never references Settings.
- **Rationale**: Keeps `api_key_missing` a pre-stream `ProblemDetails`, avoids a Core→Settings reference (§I); the endpoint is the composition boundary. The double key read is negligible (`IMemoryCache`).

## D7 — No persistence, no migration

- **Decision**: The ask is **stateless** — no `Conversation`/`Message` entity, no write, no migration. Persisting the question/answer/used passages is **US-18**.
- **Rationale**: Keeps US-14 focused on the answer pipeline; US-18 owns history.

## Open items deferred (not blocking)

- Conversation persistence → **US-18**. Chat UI (question field + stream render) → **US-15**. Clickable `[n]` citations → **US-16**. Refusal detection/render → **US-17**. Demo-mode credential → **US-03**.
- Re-ranking / hybrid search / query rewriting → out of scope (README simplification note).
