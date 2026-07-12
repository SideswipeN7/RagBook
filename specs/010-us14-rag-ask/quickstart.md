# Quickstart — US-14 streaming RAG ask validation guide

Proves the ask→stream pipeline end-to-end against real pgvector with a **fake streaming generator** (no test hits Anthropic; §V). The US-06 deterministic embedding fake keeps seeded chunks and the question comparable.

## Prerequisites

- .NET 10 SDK; **Docker running** (Testcontainers `pgvector/pgvector:pg17`).
- No migration (US-14 adds no table).

## Automated verification (the gate)

Run before any PR (repo memory: all-tests-green-before-pr; critical-analysis-before-pr):

```sh
dotnet test tests/RagBook.Application.Tests    # pipeline: validate/threshold/insufficient/scope; PromptBuilder: numbering/trim
dotnet test tests/RagBook.Api.IntegrationTests # POST /api/chat/ask SSE end-to-end (fake generator) + real-generator SSE parse
```

### What each scenario proves (maps to spec ACs)

| Scenario | Tier | Proves |
|---|---|---|
| valid question in scope → `sources` then streamed `token`s then `done{groundsFound:true}` | Integration | AC-1, AC-6, FR-008/009 |
| answer draws only on in-scope/session passages (reuses US-13 seeding) | Integration | AC-2, SC-002 |
| unrelated question (all below threshold) → `done{groundsFound:false}`, generator **never called** | Integration | AC-3, SC-003 |
| all-processing scope → empty-scope path, no generation | Integration | AC-3 edge (US-13 AC-5) |
| PromptBuilder numbers `[1..K]` with file+page, trims weakest past `MaxContextChars` | Application | FR-006, AC-4 |
| threshold cutoff = `1 − SimilarityThreshold` on distance | Application | FR-005, AC-4 |
| empty / >2000-char question → 400 `chat.invalid_question` (no retrieval) | Integration | FR-002, SC-007 |
| no key → 401 `settings.api_key_missing` before any provider call | Integration | FR-003, AC-5.1 |
| generator fails before first delta → ProblemDetails with mapped code (invalid key / 429 / 503) | Integration | AC-5.2 |
| generator fails mid-stream → `error` event with mapped code | Integration | AC-5.3 |
| real `AnthropicAnswerGenerator`: canned SSE body → deltas parsed; 401/429/5xx → mapped failure | Integration | D2 (offline, no real Anthropic) |

**Fakes**: `FakeStreamingAnswerGenerator` yields scripted deltas (or throws a scripted `AnswerGenerationFailure`, before or after the first delta). Retrieval seeding reuses US-13's `ChatRetrievalSeed`.

## Manual smoke (optional, real key)

`dotnet run --project src/RagBook.AppHost`, set a real key (US-02 settings), index a document (US-04/06), then:
```sh
curl -N -X POST https://localhost:.../api/chat/ask -H 'Content-Type: application/json' \
  --cookie 'ragbook_session=…' \
  -d '{"question":"…","scope":{"type":"all"}}'
```
Expect a `sources` event then incremental `token` events then `done`.

## Non-goals in this guide

- Chat UI / stream rendering (US-15), clickable citations (US-16), refusal UX (US-17), history (US-18).
