# Quickstart — US-16 citations validation guide

Proves clickable, verifiable citations over US-14's stream + US-15's chat. No test hits Anthropic (§V).

## Prerequisites

- Node + Angular toolchain (frontend); .NET 10 + Docker (backend integration). No migration.

## Automated verification (the gate)

```sh
cd src/Web && npm test                            # citation-parser + chat-answer (clickable [n], used/searched, preview)
dotnet test tests/RagBook.Application.Tests        # PromptBuilder carries ChunkId
dotnet test tests/RagBook.Api.IntegrationTests     # the sources event carries text + chunkId
```

### What each scenario proves (maps to spec ACs)

| Scenario | Tier | Proves |
|---|---|---|
| `citation-parser` splits text + `[n]`; adjacent `[1][2]`, repeated, mid-sentence preserved | Web | FR-003 edges |
| out-of-range `[n]` stays plain text + warns | Web | FR-006, SC-005 |
| `[2]` clickable → preview shows passage 2 full text + file + page | Web | AC-2, SC-002 |
| used sources highlighted; the rest under a collapsed "pozostałe przeszukane fragmenty" | Web | AC-1, SC-004 |
| no markers used → all sources shown with a "none cited" note + warning | Web | FR-007 |
| preview still shows the passage after the source document is "deleted" | Web | AC-4, SC-006 |
| a `groundsFound:false` answer shows no used-source list | Web | AC-5, FR-009 |
| the `sources` SSE event includes `text` + `chunkId` | Integration | FR-001 |
| `PromptBuilder` output carries `ChunkId` (deterministic mapping data) | Application | FR-002 |

## Manual smoke (optional, real key)

`dotnet run --project src/RagBook.AppHost`; ask a grounded question; the answer shows clickable `[n]`; clicking one opens the passage preview; used vs searched sources are separated.

## Non-goals in this guide

- Original-PDF navigation/highlighting; answer export; cross-reload persistence + "document deleted" banner (US-18); refusal detection (US-17).
