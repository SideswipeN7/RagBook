# Implementation Plan: Zakres pytania — scoped retrieval (US-13)

**Branch**: `009-us13-scope` (git: `fm/us13-scope`) | **Date**: 2026-07-12 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/009-us13-scope/spec.md`

## Summary

A new **`Chat`** module delivers the **scoped retrieval engine**: given a `ChatScope` (all / folder / document) and a **question**, it returns the most relevant chunks within that scope, ready to cite. The engine — `IScopedRetriever` (Chat/Domain) implemented by `ScopedRetriever` (Infrastructure) — does, in order: (1) **validate/resolve** the scope against the session (folder/document not visible → `chat.scope_not_found`); (2) a **cheap metadata emptiness check** (any Ready document with chunks in scope?) — if empty, return the empty outcome **without embedding or vector search** (AC-5); (3) embed the question via the US-06 `IEmbeddingProvider` seam (deterministic fake in dev/tests); (4) a **raw-SQL pgvector search** (`embedding <=> query` cosine, `ORDER BY … LIMIT TopK`) pre-filtered by session + `status = Ready` + the scope predicate (folder subtree via `f.path LIKE @scopePath || '%'`, document via `d.id = @id`). `TopK` comes from a new config-driven `RagOptions`. No new persisted entity, **no migration** — the engine reads the US-06/US-07/US-09 tables. The scope selector UI and conversation persistence are **US-14**.

## Technical Context

**Language/Version**: C# (net10.0, LangVersion preview).

**Primary Dependencies**: EF Core 10 + Npgsql (raw SQL via `dbContext.Database`), `Pgvector` (the `Vector` type + `<=>` cosine operator from the US-06 HNSW `vector_cosine_ops` index), the US-06 `IEmbeddingProvider` seam. No new packages.

**Storage**: PostgreSQL + pgvector — **read-only** for this feature (chunks/documents/folders from US-06/04/09). No new table, **no migration**.

**Testing**: xUnit + FluentAssertions (Domain: `ChatScope`); Testcontainers `pgvector/pgvector:pg17` (Integration: the retrieval query over seeded ready documents + chunks with deterministic fake embeddings — the mandatory subtree prefix-match test lives here). No external provider is hit (the deterministic `FakeEmbeddingProvider` embeds the query).

**Target Platform**: Linux container (GCP Cloud Run).

**Project Type**: Web (modular-monolith .NET backend); no frontend in US-13 (UI is US-14).

**Performance Goals**: Retrieval is one HNSW-indexed vector search (`<=>`) over a session-filtered, scope-filtered set, capped at `TopK`. Empty scopes cost a single metadata `EXISTS` and nothing else.

**Constraints**: Session isolation enforced **explicitly** in raw SQL (`d.user_session_id = @session` — the EF global query filter does not apply to raw SQL, per US-06). Ready-only (`d.status = 1`). `TopK` config-driven (no magic number).

**Scale/Scope**: Per-session document sets; HNSW keeps search sub-linear.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Assessment |
|---|---|
| I. Vertical-slice modular monolith | ✅ New `Modules/Chat/` with `Domain/` + `Errors/` (+ `RagOptions`). The raw-SQL impl lives in Infrastructure `SharedContext/`. Chat references neither Documents nor Folders modules directly — it reads tables via SQL and resolves a folder path through the existing `IFolderRepository` seam. |
| II. CQRS + Result contract | ✅ The retriever returns `Result<ScopedRetrievalResult>`; failure = `chat.scope_not_found` from the closed `ChatErrors` catalog. No throwing for expected failures. (No Wolverine query/endpoint yet — the engine is a seam US-14 will dispatch behind a chat query.) |
| III. Data isolation by session | ✅ Every query (emptiness check + search) filters `d.user_session_id = @session` explicitly (raw SQL); folder/document scope targets are resolved **through the session**, so a cross-session target → `chat.scope_not_found`. Proven by an isolation integration test. |
| IV. Test-First (Red→Green) | ✅ Domain test for `ChatScope`; Integration tests (Testcontainers) for the SQL the engine runs — the tier that "compiles and executes the heavy queries unit tests cannot". Mandatory subtree prefix-match test. |
| V. External providers — resilience + cache | ✅ Reuses the US-06 `IEmbeddingProvider` seam (one model for the whole index); tests use its deterministic fake. No new external call. |
| VI. Auditing & time | ✅ No writes; N/A (read-only engine). |
| VII. Secrets | ✅ N/A (the app-key embedding provider is US-06; the user's generation key is US-02). |
| VIII. Operations & delivery | ✅ No migration, no startup change. `TopK` in `RagOptions` (config-driven). CI runs all tiers. |
| IX. Frontend & design system | ✅ N/A — no UI in US-13 (the selector is US-14). |

**No deviations.** No Complexity Tracking entries required.

## Project Structure

### Documentation (this feature)

```text
specs/009-us13-scope/
├── plan.md              # This file
├── research.md          # Phase 0 — decisions (query shape, scope resolution, emptiness, seam)
├── data-model.md        # Phase 1 — ChatScope, ScopedRetrievalResult, RetrievedChunk, RagOptions, error
├── quickstart.md        # Phase 1 — how the mandatory tests prove the ACs
├── contracts/
│   └── retrieval-seam.md # Phase 1 — the IScopedRetriever seam contract (internal; US-14 consumes it)
└── tasks.md             # Phase 2 (/speckit-tasks)
```

### Source Code (repository root)

```text
src/RagBook/Modules/Chat/                          # NEW module (Core)
├── Errors/ChatErrors.cs                            # chat.scope_not_found (NotFound → 404)
├── RagOptions.cs                                   # SectionName="Rag"; TopK (config-driven)
└── Domain/
    ├── ChatScope.cs                                # { ChatScopeType Type; Guid? TargetId } + factory/guards
    ├── ChatScopeType.cs                            # All | Folder | Document
    ├── IScopedRetriever.cs                         # RetrieveAsync(ChatScope, string question, ct) → Result<ScopedRetrievalResult>
    ├── ScopedRetrievalResult.cs                    # IsEmptyScope + IReadOnlyList<RetrievedChunk> Matches
    └── RetrievedChunk.cs                           # ChunkId, DocumentId, FileName, Text, PageNumber, Distance

src/RagBook.Infrastructure/SharedContext/Retrieval/ # NEW folder
└── ScopedRetriever.cs                              # IScopedRetriever impl: validate scope → emptiness check
                                                    #  → embed (IEmbeddingProvider) → raw-SQL <=> search → map rows
# DI: register AddScoped<IScopedRetriever, ScopedRetriever>() in RagBook.Infrastructure/DependencyInjection.cs
# Config: Configure<RagOptions>(...) in Program.cs; "Rag": { "TopK": 8 } in appsettings.json

tests/
├── RagBook.Domain.Tests/Chat/ChatScopeTests.cs                    # scope construction/guards
└── RagBook.Api.IntegrationTests/Chat/                             # Testcontainers pgvector
    ├── ScopedRetrievalTests.cs         # folder subtree (MANDATORY), document isolation, all/ready-only,
    │                                   #   session isolation, TopK limit + ordering, scope_not_found
    ├── EmptyScopeTests.cs              # empty scope → no embedding (counting fake) + no search
    └── ChatRetrievalSeed.cs           # seeds folders + Ready documents + chunks (raw-SQL insert, fake embeddings)
```

**Structure Decision**: Web modular-monolith. US-13 introduces the `Chat` module as a **pure engine** (seam + value + config), no endpoint/UI/migration. It mirrors the US-06 raw-SQL-vector conventions (`dbContext.Database`, explicit session filter, `CAST([…] AS vector)`, `<=>` cosine) on the **read** side, and reuses the US-06 `IEmbeddingProvider` seam to embed the query. US-14 will add the chat command/endpoint that dispatches this retriever, persists the scope on a conversation, and renders the selector.

## Complexity Tracking

*No entries — the design adds no new top-level project, no migration, and no principle deviation.*
