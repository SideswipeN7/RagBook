# Tasks: Zakres pytania — scoped retrieval (US-13)

**Input**: Design documents from `specs/009-us13-scope/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/retrieval-seam.md, quickstart.md

**Tests**: Included — the constitution mandates Test-First (Red→Green→Refactor). Every behavior lands via a
failing test first, at the cheapest tier that proves it (Domain for `ChatScope`; Testcontainers Integration
for the retrieval query — the tier that executes the heavy pgvector SQL).

**Organization**: New greenfield `Chat` module delivering the **retrieval engine** (seam + value + config —
no endpoint/UI/migration; that is US-14). One Setup phase (config + errors), one Foundational phase (scope
value + seam + result types + seed helper — BLOCK the stories), then the stories: US1 = folder subtree
(AC-2) 🎯 MVP, US2 = document scope (AC-3), US3 = all/ready-only + isolation + TopK (US3), US4 = empty
short-circuit (AC-5), US5 = scope validation / not-found (AC-4-analogue).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel (different files, no incomplete dependency).
- Paths follow plan.md (`src/RagBook/Modules/Chat`, `src/RagBook.Infrastructure/SharedContext/Retrieval`, `tests/…`).

---

## Phase 1: Setup

- [X] T001 [P] Add `RagOptions` (`src/RagBook/Modules/Chat/RagOptions.cs`, `SectionName="Rag"`, `TopK` default 8); bind `Configure<RagOptions>(...)` in `src/RagBook.API/Program.cs`; add `"Rag": { "TopK": 8 }` to `src/RagBook.API/appsettings.json`.
- [X] T002 [P] Define closed catalog `ChatErrors` with `chat.scope_not_found` (`Error.NotFound` → 404) in `src/RagBook/Modules/Chat/Errors/ChatErrors.cs`.

**Checkpoint**: Solution builds; `Rag:TopK` binds; the not-found code exists.

---

## Phase 2: Foundational (scope value + seam — BLOCK the stories)

- [X] T003 [P] Domain test (Red): `ChatScopeTests` — `Should_HaveNoTarget_When_All`, `Should_RequireTarget_When_FolderOrDocument`, `Should_Reject_When_AllWithTarget` — in `tests/RagBook.Domain.Tests/Chat/ChatScopeTests.cs`.
- [X] T004 Implement `ChatScopeType` (All/Folder/Document) and `ChatScope` (factories `All()`/`Folder(id)`/`Document(id)` + construction guards) in `src/RagBook/Modules/Chat/Domain/` (Green for T003).
- [X] T005 [P] Define result shapes `RetrievedChunk` (ChunkId, DocumentId, FileName, Text, PageNumber, Distance) and `ScopedRetrievalResult` (`IsEmptyScope` + `Matches`; `Empty` / `From(matches)`) in `src/RagBook/Modules/Chat/Domain/`.
- [X] T006 [P] Define the seam `IScopedRetriever.RetrieveAsync(ChatScope scope, string question, CancellationToken) → Task<Result<ScopedRetrievalResult>>` in `src/RagBook/Modules/Chat/Domain/IScopedRetriever.cs`.
- [X] T007 [P] Integration test infrastructure: `ChatRetrievalSeed` (seed folders via `FolderPath`, **Ready** documents via EF metadata like `TreeSeed`, chunks via the raw-SQL `CAST([…] AS vector)` insert with `FakeEmbeddingProvider` embeddings) and `CountingEmbeddingProvider` (wraps/records `EmbedBatchAsync` calls) in `tests/RagBook.Api.IntegrationTests/Chat/`.

**Checkpoint**: `ChatScope` green; seam + result types compile; the seed helper can create scoped test data.

---

## Phase 3: User Story 1 — Folder scope covers the subtree (Priority: P1) 🎯 MVP

**Goal**: A folder scope returns passages from that folder **and every nested subfolder**, and nothing outside it.

**Independent test**: Seed "Umowy" + "Umowy/2026" (each a Ready doc + chunks) and a sibling "Faktury"; retrieve scoped to "Umowy" → passages from both nested docs, none from the sibling.

- [X] T008 [US1] Integration test (Red→Green): `Should_ReturnSubtreePassages_When_FolderScope` (**mandatory** prefix match: parent + subfolder) and `Should_ExcludeSiblingFolder_When_FolderScope` — in `tests/RagBook.Api.IntegrationTests/Chat/ScopedRetrievalTests.cs`.
- [X] T009 [US1] Implement `ScopedRetriever : IScopedRetriever` in `src/RagBook.Infrastructure/SharedContext/Retrieval/ScopedRetriever.cs`: resolve a Folder scope via `IFolderRepository.GetByIdAsync` (its `Path` → `@scopePath`); **emptiness `EXISTS` check** (session + `status=1` + scope predicate) — empty → `ScopedRetrievalResult.Empty` with no embed/search; else embed the question via `IEmbeddingProvider.EmbedBatchAsync([question])` and run the raw-SQL `<=>` cosine search (`f.path LIKE @scopePath || '%'`, `ORDER BY … LIMIT RagOptions.TopK`) via `dbContext.Database.GetDbConnection()`, mapping rows to `RetrievedChunk`. **Parameterize** session id, scope target id, `scopePath`, and TopK as `NpgsqlParameter`s; pass the query vector as a **text parameter** `CAST(@queryVec AS vector)` (the `[v1,…]` literal bound as a param, not string-concatenated) — no user value is interpolated into the SQL (U1). Register `AddScoped<IScopedRetriever, ScopedRetriever>()` in `src/RagBook.Infrastructure/DependencyInjection.cs` (Green for T008).

**Checkpoint**: AC-2 demonstrable — a folder scope draws on its whole subtree, excludes siblings. MVP.

---

## Phase 4: User Story 2 — Document scope limits to one file (Priority: P1)

**Goal**: A document scope returns passages only from that document.

**Independent test**: With several Ready documents, retrieve scoped to document A → every passage belongs to A, none to B/C.

- [X] T010 [US2] Integration test (Red→Green): `Should_ReturnOnlyThatDocument_When_DocumentScope`, `Should_ExcludeOtherDocuments_When_DocumentScope`, and `Should_SurfacePageNumber_When_ChunkHasPage` (a PDF chunk's `page_number` propagates to `RetrievedChunk.PageNumber`; a TXT chunk yields `null`) — in `ScopedRetrievalTests.cs` (C1: proves the page/location survives the raw-SQL mapping, forward-looking for US-16 citations).
- [X] T011 [US2] Extend `ScopedRetriever` with the Document scope: validate the target exists in the session (cheap `SELECT 1 … WHERE id=@id AND user_session_id=@session`; absent → deferred to US5), predicate `d.id = @documentId` (Green for T010).

**Checkpoint**: AC-3 — a document scope never leaks another document's passages.

---

## Phase 5: User Story 3 — All scope: ready-only, isolated, bounded (Priority: P1)

**Goal**: The default scope searches every Ready document in the session, never processing/failed docs, never another session's, capped at `TopK`, ordered closest-first.

**Independent test**: Seed Ready + processing + failed docs (and a second session's Ready doc); retrieve in All scope → only the current session's Ready docs contribute; ≤ TopK, ordered by distance.

- [X] T012 [US3] Integration test (Red→Green): `Should_ExcludeProcessingAndFailed_When_AllScope`, `Should_ExcludeOtherSession_When_AllScope`, `Should_CapAtTopK_And_OrderByDistance_When_ManyMatches`, and `Should_ReturnAllMatches_When_FewerThanTopK` (N < TopK eligible → exactly N results, no padding — FR-007 second half, A1) — in `ScopedRetrievalTests.cs`. (The All-scope predicate + session/status filter + `LIMIT TopK`/order are already in T009; this story asserts them.)

**Checkpoint**: US3 — ready-only, session-isolated, bounded, ordered.

---

## Phase 6: User Story 4 — Empty scope short-circuits (Priority: P1)

**Goal**: A scope with no Ready-indexed content returns `IsEmptyScope` immediately, without embedding or a vector search.

**Independent test**: Retrieve within a folder that has no Ready documents → `IsEmptyScope`, and the embedding provider was never called.

- [X] T013 [US4] Integration test (Red→Green): `Should_ReturnEmptyScope_Without_Embedding_When_NoReadyDocs` — using `CountingEmbeddingProvider`, retrieve a folder scope with only processing/failed (or no) documents → `result.Value.IsEmptyScope` true, zero matches, and `EmbedBatchAsync` call count is **0**; a control case over a non-empty scope asserts the count is **1** — in `tests/RagBook.Api.IntegrationTests/Chat/EmptyScopeTests.cs`. (The `EXISTS` short-circuit is already in T009; this story proves it saves the embedding.)

**Checkpoint**: AC-5 — empty scope costs neither an embedding nor a search.

---

## Phase 7: User Story 5 — Scope validation / not-found (Priority: P2)

**Goal**: A Folder/Document scope naming a target not visible to the session fails `chat.scope_not_found`, searching nothing.

**Independent test**: Retrieve with a folder/document id from another session (or a deleted one) → `Failure(chat.scope_not_found)`, no passages.

- [X] T014 [US5] Integration test (Red→Green): `Should_Return_ScopeNotFound_When_FolderFromAnotherSession`, `Should_Return_ScopeNotFound_When_DocumentDeleted` — in `ScopedRetrievalTests.cs`.
- [X] T015 [US5] Ensure `ScopedRetriever` returns `Result.Failure(ChatErrors.ScopeNotFound)` when the Folder (`GetByIdAsync` null) or Document (session existence check absent) target is not visible — before any embedding/search (Green for T014).

**Checkpoint**: US5 — invisible scope target is a clean 404, never a widened search.

---

## Phase 8: Polish & cross-cutting

- [X] T016 [P] Docs: add a **"Hybrid filtering (US-13)"** section to `README.md` (pre-filter session + `status=Ready` + scope predicate — folder subtree via materialized-path prefix, document by id — then pgvector `<=>` cosine `LIMIT TopK`; empty scope short-circuits before embedding; `Rag:TopK` config), and record durable notes in `AGENTS.md` (`Chat` module = scoped retrieval engine; `IScopedRetriever` seam; raw-SQL read mirrors US-06; `status=1` not `'Ready'`; UI/persistence are US-14).
- [X] T017 Full green run: `dotnet test tests/RagBook.Domain.Tests` and `dotnet test tests/RagBook.Api.IntegrationTests` (Testcontainers); then the critical-analysis pass on the diff before opening the PR (repo memory: critical-analysis-before-pr). If Smart App Control blocks local test hosts, push and let CI be the green gate.

---

## Dependencies & execution order

- **Setup (T001–T002)** → **Foundational (T003–T007)** block the stories.
- **US1 (T008–T009)** builds the retriever (All + Folder + emptiness + embed + search + TopK/order) and is the MVP. **US2 (T010–T011)** adds the Document predicate; **US3 (T012)** asserts ready-only/isolation/bounds already in place; **US4 (T013)** proves the empty short-circuit; **US5 (T014–T015)** adds not-found validation.
- Within a story, the test precedes its implementation; `[P]` tasks touch different files.
- Polish (T016–T017) after the stories are green.

## Parallel example (Foundational)

T003 (scope test), T005 (result types), T006 (seam), T007 (seed helper) are independent files and can run in parallel; T004 (scope impl) follows T003.

## MVP scope

**US1 (T001–T009)** yields the demonstrable increment: a folder-scoped retrieval that draws on the whole
subtree (and nothing outside it), backed by the real pgvector search with the empty-scope short-circuit.
US2–US5 add the document scope, the all-scope guarantees, the empty-scope proof, and the not-found
validation.
