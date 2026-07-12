# Quickstart — US-15 streaming chat UI validation guide

Proves the chat surface consumes US-14's SSE and the backend hardening, with a stubbed `fetch` (frontend) and the US-14 fake generator (backend). No test hits Anthropic (§V).

## Prerequisites

- Node + Angular toolchain (frontend); .NET 10 + Docker (backend integration).
- No migration.

## Automated verification (the gate)

Run before any PR (repo memory: all-tests-green-before-pr; critical-analysis-before-pr):

```sh
cd src/Web && npm test                            # ChatStore (stubbed fetch), sse-parser, chat + scope-selector components
dotnet test tests/RagBook.Api.IntegrationTests    # heartbeat + client-abort cancellation (fake generator)
```

### What each scenario proves (maps to spec ACs)

| Scenario | Tier | Proves |
|---|---|---|
| `token` events append incrementally (answer grows multiple times before done) | Web | AC-1, SC-001 |
| events consumed in order `sources` → `token` → `done`; source list set on `sources` | Web | AC-4, FR-002 |
| `stop()` aborts, marks `interrupted`, keeps partial answer | Web | AC-2, SC-002 (client half) |
| `error` event → error state + mapped PL message + Try again | Web | AC-3, FR-005 |
| stream ends without `done` → treated as error | Web | AC-3 edge |
| second `ask` while streaming aborts the first (one active) | Web | FR-008, SC-005 |
| `done{groundsFound:false}` → neutral no-basis note, no answer text | Web | AC-7 |
| no key → input disabled + link to settings (reuses `chatLocked`) | Web | AC-7 |
| scope selector offers All / folder / **ready** doc; chip + scope sent | Web | AC-6, SC-004 |
| `sse-parser` splits an event across chunk boundaries | Web | D1 correctness |
| client aborts mid-stream → the fake generator observes cancellation | Integration | AC-2/AC-5, FR-004, SC-002 (server half) |
| slow generation → a keep-alive comment is emitted | Integration | FR-010, SC-006 |

## Manual smoke (optional, real key)

`dotnet run --project src/RagBook.AppHost`; set a key (US-02), index a document (US-04/06), open the chat, pick a scope, ask — the answer streams token by token; **Stop** halts it; a proxy does not cut a long answer.

## Non-goals in this guide

- Clickable citations (US-16), full refusal UX (US-17), history persistence (US-18), stream resumption / WebSocket.
