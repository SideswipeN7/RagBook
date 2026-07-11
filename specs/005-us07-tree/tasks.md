# Tasks: Folder & Document Tree (Drzewo folderów i lista dokumentów)

**Input**: Design documents from `specs/005-us07-tree/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/tree-api.md, quickstart.md

**Tests**: Included — the constitution mandates Test-First (Red→Green→Refactor); every behavior lands
via a failing test first, at the cheapest tier that proves it (Application/Web unit → Integration).

**Organization**: US-07 is a read-projection + presentation story. The backend read (`GET /api/tree`,
`ITreeReader`, DTOs), the `FailureReason` column, and the frontend foundation (`@angular/cdk`, `TreeStore`,
size util) are Foundational — every UI story reads through them. Stories: US1 = unified tree + hierarchy +
expansion (AC-1), US2 = document metadata rows (AC-2), US3 = empty state (AC-3), US4 = refresh without
reload (AC-4). AC-5 (single request, no N+1) is a Foundational backend guarantee, asserted there.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel (different files, no incomplete dependency).
- Paths follow plan.md (`src/RagBook/Modules/Tree`, `src/RagBook.API`, `src/RagBook.Infrastructure`, `src/Web`, `tests/…`).

---

## Phase 1: Setup

- [X] T001 Add `@angular/cdk` (`^20`, matching Angular 20) to `src/Web/package.json` and `npm install`; verify `cdk-tree` imports resolve.

**Checkpoint**: Web builds with `@angular/cdk` available.

---

## Phase 2: Foundational (backend read + schema + frontend foundation — BLOCKS all stories)

### Schema — nullable FailureReason (forward-looking)

- [X] T002 Add nullable `FailureReason` (`string?`) to the `Document` aggregate in `src/RagBook/Modules/Documents/Domain/Document.cs` (private setter; no US-07 write path — US-06 fills it).
- [X] T003 Map `failure_reason text NULL` in `src/RagBook.Infrastructure/SharedContext/Persistence/Configurations/DocumentConfiguration.cs`; create migration `AddDocumentFailureReason` in `src/RagBook.Infrastructure.Migrations` (applied out-of-band).

### Tree read slice (Red → Green)

- [X] T004 [P] Application test (Red): `GetTreeQueryHandler` maps a mocked `ITreeReader` `TreeData` into `TreeResponse`, preserving the reader's ordering (folders A→Z, documents newest-first) — in `tests/RagBook.Application.Tests/Tree/GetTreeQueryHandlerTests.cs`.
- [X] T005 [P] Define `ITreeReader.GetAsync(ct)` → `TreeData(IReadOnlyList<TreeFolder>, IReadOnlyList<TreeDocument>)` in `src/RagBook/Modules/Tree/Domain/`, and the DTOs `TreeFolder`, `TreeDocument`, `TreeResponse` in `src/RagBook/Modules/Tree/Features/GetTree/`.
- [X] T006 Implement `GetTreeQuery : IQuery<TreeResponse>` and `GetTreeQueryHandler` (calls `ITreeReader.GetAsync`, maps to `TreeResponse`) in `src/RagBook/Modules/Tree/Features/GetTree/` (Green for T004).
- [X] T007 Implement `TreeReader : ITreeReader` — two `AsNoTracking` session-scoped queries: folders `OrderBy(LOWER(name))`, documents `Where(Origin != Demo).OrderByDescending(UploadedAt)`; project to `TreeFolder`/`TreeDocument` (folder depth from `FolderPath`) — in `src/RagBook.Infrastructure/SharedContext/Persistence/TreeReader.cs`; register `AddScoped<ITreeReader, TreeReader>()` in `src/RagBook.Infrastructure/DependencyInjection.cs`.
- [X] T008 Implement `GET /api/tree` in `src/RagBook.API/Endpoints/TreeEndpoints.cs` (dispatch `GetTreeQuery` → 200 `TreeResponse`); map `MapTreeEndpoints()` in `Program.cs`.
- [X] T009 Integration test (Red→Green): `Should_ReturnFoldersAndDocuments_InOneResponse` (AC-5 — one request, both lists), `Should_OrderFoldersAlphabeticallyAndDocumentsByDateDesc` (FR-008), `Should_ExcludeOtherSessionsData` (FR-012), `Should_ExcludeDemoDocuments` (FR-013) — in `tests/RagBook.Api.IntegrationTests/Tree/TreeEndpointTests.cs`.

### Frontend foundation

- [X] T010 [P] Web unit test (Red) + impl: `formatFileSize(bytes)` — decimal `B`/`KB`/`MB`, 1 dp (1 MB = 1,000,000) — in `src/Web/src/app/core/file-size.ts` (+ `file-size.spec.ts`).
- [X] T011 [P] Implement `TreeStore` (signals): fetch `GET /api/tree`, compose `TreeNode[]` (folders by `parentId`, documents by `folderId`, root documents top-level), `expanded: Set<string>` persisted to `sessionStorage`, `toggle(id)`, `refresh()` — in `src/Web/src/app/core/tree.store.ts` (+ `tree.store.spec.ts` asserting compose + expansion persistence).

**Checkpoint**: `GET /api/tree` returns both lists in one request, ordered + isolated; `TreeStore` composes the nested tree; size util + column ready.

---

## Phase 3: User Story 1 — See the knowledge base as one tree (AC-1) 🎯 MVP

**Goal**: A single `cdk-tree` renders folders nested with their documents inside and root documents at the top; folders expand/collapse; expansion survives in-session navigation.

**Independent test**: seed folders `A`, `A/B` + documents in each level and root → tree mirrors the hierarchy; collapse a folder, navigate away and back → still collapsed.

- [X] T012 [P] [US1] Web component test (Red): `app-document-tree` renders the nested hierarchy from `TreeStore` (folders nested, documents under their folder, root documents at top); collapsing a folder hides its children and persists via `TreeStore` — in `src/Web/src/app/documents/tree/document-tree.spec.ts`.
- [X] T013 [US1] Implement `app-document-tree` (standalone, OnPush, `cdkTree` + `NestedTreeControl`) rendering folder nodes and document leaves from `TreeStore.roots`; expand/collapse via `TreeStore.toggle` (sessionStorage) using design tokens — in `src/Web/src/app/documents/tree/document-tree.{ts,html,scss}` (Green for T012).
- [X] T014 [US1] Re-provide the US-09 folder actions (create/rename/delete, delegating to `FolderTreeStore`) on folder nodes in `app-document-tree`; render the component in `app.html` in place of `app-folder-tree`. **After each successful folder mutation call `TreeStore.refresh()`** (not only `FolderTreeStore.refresh()`) — the unified tree reads from `TreeStore`/`GET /api/tree`, so folder create/rename/delete must re-read the tree or it goes stale (AC-4/FR-009, analyze finding I1). Cover with a test: creating a folder triggers a `GET /api/tree` refresh.
- [X] T015 [P] [US1] Web test: an empty folder is expandable and reveals an "empty folder" note (FR-011) — extend `document-tree.spec.ts`.

**Checkpoint**: AC-1 demonstrable — one tree, correct hierarchy, expansion persists. MVP.

---

## Phase 4: User Story 2 — Read each document's metadata (AC-2)

**Goal**: Each document row shows name, human-readable size, status badge, chunk count, upload date; processing → spinner, failed → error + reason tooltip, ready → chunk count.

**Independent test**: seed documents in each status → each row shows the correct metadata; processing shows a spinner; failed reveals its reason on hover.

- [X] T016 [P] [US2] Web component test (Red): document row shows name, `formatFileSize` size, status badge, chunk count, date; `Processing` → spinner (no chunk count); `Failed` → error indicator with the reason (and a generic message when the reason is null); `Ready` → chunk count — in `src/Web/src/app/documents/tree/document-row.spec.ts`.
- [X] T017 [US2] Implement the document-row template/component (status badge/spinner/error, `formatFileSize`, date, chunk count; `displayFailureReason` fallback) using design tokens in `src/Web/src/app/documents/tree/` (Green for T016).
- [X] T018 [P] [US2] Web test + impl: a long file/folder name truncates with the full name in a `title` tooltip (FR-010) — extend the row/folder templates + test.

**Checkpoint**: AC-2 demonstrable — every status renders its metadata distinctly.

---

## Phase 5: User Story 3 — Empty state for a fresh session (AC-3)

**Goal**: A session with no folders and no documents shows an empty state with an "upload your first document" CTA and a demo-mode pointer.

**Independent test**: open the view for a brand-new session → empty state with CTA + demo pointer, no tree rows.

- [X] T019 [P] [US3] Web component test (Red): when `TreeStore` has no folders and no documents, `app-document-tree` shows the empty state (CTA text + demo pointer) and no tree rows — in `document-tree.spec.ts`.
- [X] T020 [US3] Implement the empty state (`@if` on empty tree) with the upload CTA + demo-mode pointer, and a labelled read-only demo placeholder section (FR-013), in `app-document-tree` (Green for T019).

**Checkpoint**: AC-3 demonstrable — inviting empty state for new visitors.

---

## Phase 6: User Story 4 — The tree stays fresh without a reload (AC-4)

**Goal**: A completed upload or deletion updates the tree without a full page reload via the shared store.

**Independent test**: with the tree open, complete an upload → the document appears without a reload.

- [X] T021 [P] [US4] Web test (Red→Green): calling `TreeStore.refresh()` re-reads `GET /api/tree` and updates `roots` (mirrors the upload/delete hook) — extend `tree.store.spec.ts`.
- [X] T022 [US4] Wire `DocumentUploadStore` (US-04) to call `TreeStore.refresh()` after a successful upload (alongside `QuotaStore.refresh()`), so the new document appears in the tree; extend `document-upload.store.ts` + its test.

**Checkpoint**: AC-4 validated — the tree reflects uploads/deletes with no reload; the refresh hook is ready for US-08 delete.

---

## Phase 7: Docs & polish (cross-cutting)

- [X] T023 Remove the superseded `app-folder-tree` component (`src/Web/src/app/folders/`) once the unified tree has folder-action parity; keep `FolderTreeStore` (still owns folder mutations) and update any imports.
- [X] T024 Update `README.md` (a "Drzewo dokumentów" note: one `GET /api/tree`, no N+1; folders A→Z / documents newest-first; `cdk-tree`; decimal size; expansion in sessionStorage; `FailureReason` forward-looking for US-06) and record durable knowledge in `AGENTS.md` (Tree read slice + single `ITreeReader` seam; `@angular/cdk` added; `documents.failure_reason` nullable filled by US-06).
- [X] T025 Full green run: `dotnet test RagBook.slnx` (Application + Testcontainers Integration) and `npm test` in `src/Web`; verify `dotnet run --project src/RagBook.AppHost` shows the unified tree with an uploaded document appearing without a reload.

---

## Dependencies & execution order

- **Setup (T001)** → **Foundational (T002–T011)** block every story.
- **US1 (T012–T015)** is the MVP (the tree itself). **US2 (T016–T018)** adds the metadata rows within
  it. **US3 (T019–T020)** the empty state. **US4 (T021–T022)** the refresh hook. US2–US4 all depend on
  US1's component existing.
- Within a phase, `[P]` tasks touch different files and may run in parallel; test tasks precede their
  implementation.
- Polish (T023–T025) after all stories are green.

## Parallel example (Foundational)

T004, T005, T010, T011 (`[P]`) touch independent files and can run together; T006/T007 follow the seam/
DTOs; T009 (integration) follows the endpoint (T008).

## MVP scope

**US1 (T001–T015)** yields a demonstrable increment: one tree showing folders + documents with correct
hierarchy and persistent expansion. US2–US4 add metadata rows, the empty state, and no-reload refresh.
