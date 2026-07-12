# Quickstart — US-13 scoped retrieval validation guide

Proves the retrieval engine end-to-end against real pgvector. No external provider is hit — the US-06 deterministic `FakeEmbeddingProvider` embeds both the seeded chunks and the query, so results are reproducible.

## Prerequisites

- .NET 10 SDK; **Docker running** (Testcontainers `pgvector/pgvector:pg17`).
- No migration needed (US-13 adds no table).

## Automated verification (the gate)

Run before any PR (repo memory: all-tests-green-before-pr; critical-analysis-before-pr):

```sh
dotnet test tests/RagBook.Domain.Tests            # ChatScope construction/guards
dotnet test tests/RagBook.Api.IntegrationTests    # the retrieval query over seeded data (pgvector)
```

### What each scenario proves (maps to spec ACs)

| Scenario | Tier | Proves |
|---|---|---|
| `ChatScope.All/Folder/Document` guards (id required/forbidden) | Domain | value integrity |
| **Folder scope returns parent + subfolder passages** (MANDATORY prefix match) | Integration | AC-2 (US1), SC-001 |
| Sibling folder's passages excluded from a folder scope | Integration | AC-2, SC-001 |
| Document scope returns only that document's passages | Integration | AC-3 (US2), SC-002 |
| All scope: processing/failed docs excluded (ready-only) | Integration | US3, SC-003 |
| All scope: another session's chunks never returned | Integration | US3/FR-012, SC-003 |
| More eligible than `TopK` → at most `TopK`, ordered closest-first | Integration | US3, SC-006 |
| Empty scope (folder with no Ready docs) → `IsEmptyScope`, **no embedding + no search** | Integration | AC-5 (US4), SC-004 |
| Cross-session / deleted folder or document target → `chat.scope_not_found` | Integration | US5, SC-005 |

**Empty-scope proof**: the test wraps `IEmbeddingProvider` in a counting double and asserts `EmbedBatchAsync` was **not** invoked for an empty scope (and **was** invoked once for a non-empty one).

**Seeding**: `ChatRetrievalSeed` seeds folders (`FolderPath` format), Ready documents (status set via EF metadata like `TreeSeed`), and chunks inserted via the same raw-SQL `CAST([…] AS vector)` path as `ChunkRepository`, with embeddings = `FakeEmbeddingProvider.Embed(text)` so a query over a known text is comparable.

## Manual smoke (optional)

There is no UI or endpoint in US-13 (that is US-14). To sanity-check manually, run a small integration test or a scratch fixture that resolves `IScopedRetriever` from the host and calls `RetrieveAsync(ChatScope.Folder(id), "…")` against a seeded database.

## Non-goals in this guide

- Scope selector UI, conversation persistence, answer generation, citations rendering — all **US-14/US-16**.
- Similarity threshold / grounding sentinel — **US-17**.
