# Tasks: Przenoszenie plików — drag & drop (US-10)

**Input**: Design documents from `specs/015-us10-drag-drop/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/move-document.md, quickstart.md

**Tests**: Included — Test-First (Constitution §IV). Domain (`MoveToFolder`), Application (handler branches),
Integration (Testcontainers: move/root/guards/chunks/isolation), Angular (optimistic+rollback store, drop, menu).

**Organization**: A small backend move slice + the frontend drag-drop interaction (the story's value). US1 =
drag→folder + optimistic 🎯 MVP; US2 = rollback; US3 = target feedback; US4 = move-to-root; US5 = menu fallback.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: parallelizable (different files, no ordering dependency).
- Paths: `src/RagBook/Modules/Documents`, `src/RagBook.Infrastructure`, `src/RagBook.API/Endpoints`, `src/Web/src/app/{core,documents}`, `tests/…`.

---

## Phase 1: Setup

- [X] T001 [P] Add `DocumentErrors.ReadOnly` (`document.read_only`, `Error.Conflict` → 409) in `src/RagBook/Modules/Documents/Errors/DocumentErrors.cs`.

---

## Phase 2: Foundational — domain move + repository (blocks the stories)

- [X] T002 [P] Domain test (Red): `DocumentMoveTests` — `Document.MoveToFolder(folderId)` sets `FolderId` to a folder id and to `null` (root) — in `tests/RagBook.Domain.Tests/Documents/DocumentMoveTests.cs`.
- [X] T003 Domain (Green): add `Document.MoveToFolder(Guid? folderId)` (sets the private `FolderId`) — in `src/RagBook/Modules/Documents/Domain/Document.cs`.
- [X] T004 `IDocumentMoveRepository` (`GetByIdAsync` session-filtered tracked + `SaveChangesAsync`) in `src/RagBook/Modules/Documents/Domain/` + `DocumentMoveRepository` (EF, reads through the session filter) in `src/RagBook.Infrastructure/SharedContext/Persistence/DocumentMoveRepository.cs` + DI registration in `RagBook.Infrastructure/DependencyInjection.cs`.

**Checkpoint**: the aggregate can change its folder; the repo can load+save it under the session filter.

---

## Phase 3: User Story 1 — Drag a document onto a folder (Priority: P1) 🎯 MVP

**Goal**: A dropped document appears in its target folder immediately and the move persists.

**Independent test**: `PATCH /api/documents/{id}/folder` moves a document into a folder; the frontend shows it
there before the response.

- [X] T005 [US1] Application test (Red): `MoveDocumentHandlerTests` — a valid move calls `MoveToFolder`+save; a document not in the session → `document.not_found`; a `Demo`-origin document → `document.read_only`; a missing target folder → `folder.not_found`; a move to the folder it is already in → success with **no** save (no-op) — in `tests/RagBook.Application.Tests/Documents/MoveDocumentHandlerTests.cs`.
- [X] T006 [US1] `MoveDocumentCommand(Guid DocumentId, Guid? TargetFolderId) : ICommand` + `MoveDocumentCommandHandler(IDocumentMoveRepository, IFolderReference)` implementing the D1 order (Green) — in `src/RagBook/Modules/Documents/Features/MoveDocument/*`.
- [X] T007 [US1] Endpoint `PATCH /api/documents/{id}/folder` (body `MoveDocumentRequest(Guid? FolderId)`) → `Result` → 204 / ProblemDetails — in `src/RagBook.API/Endpoints/DocumentEndpoints.cs` (+ contract in `DocumentContracts`/inline).
- [X] T008 [US1] Integration test: `PATCH …/folder` moves a document into a folder (persisted); a document's **chunk count is unchanged** after the move (SC-003); a **Processing**-status document is movable (FR-007, A1) — in `tests/RagBook.Api.IntegrationTests/Documents/MoveDocumentEndpointTests.cs`.
- [X] T009 [US1] Frontend store (Red→Green): **refactor `TreeStore`** so the raw `folders`+`documents` live in signals and `roots` is **derived** (computed) from them — not a one-shot composition — so a folder-id change recomposes the tree (A2). Add `moveDocument(documentId, targetFolderId: string \| null)` — **optimistically** rewrite the document's `folderId` in the documents signal (tree recomposes) then `PATCH`; a drop onto the current folder is a client no-op (no request) — in `src/Web/src/app/core/tree.store.ts` + `tree.store.spec.ts` (optimistic move applied immediately; no-op issues no request). Keep the existing `refresh()`/expansion behaviour intact.
- [X] T010 [US1] Frontend drag-drop: document rows are `cdkDrag`; folder nodes are `cdkDropList` (via `cdkDropListGroup`); `(cdkDropListDropped)` resolves the folder id and calls `moveDocument` — in `src/Web/src/app/documents/tree/{document-tree,document-row}.*` + a `document-tree.spec.ts` case that a drop onto a folder calls `moveDocument` with that folder id.

**Checkpoint**: AC-1 — drag onto a folder moves the document, instantly and persisted. MVP.

---

## Phase 4: User Story 2 — Rollback on failure (Priority: P1)

**Goal**: A rejected move snaps the document back and explains why.

**Independent test**: A failed `PATCH` reverts the optimistic move and surfaces a notice.

- [X] T011 [US2] Frontend test + impl: `TreeStore.moveDocument` **reverts** the document's `folderId` when the `PATCH` fails and sets a `moveError` signal with a code-mapped reason; `document-tree` renders it as a design-system notice (`role="alert"`, no native dialog) — in `src/Web/src/app/core/tree.store.ts` (+ spec: failed move → original folder restored + error set) and `document-tree.*`.

**Checkpoint**: AC-2 — optimism is safe; failures roll back visibly.

---

## Phase 5: User Story 3 — Drop-target feedback (Priority: P2)

**Goal**: Valid targets highlight during a drag; invalid ones stay inert.

**Independent test**: A folder node / root zone gets the highlight class on `cdkDropListEntered`; the demo section
and the dragged row are not drop lists.

- [X] T012 [US3] Frontend: highlight a drop target on `cdkDropListEntered`/`Exited` via a design-token class; ensure the dragged document and any demo section are not drop targets — in `src/Web/src/app/documents/tree/document-tree.{html,scss,ts}` + a spec asserting the highlight toggles on enter/exit.

**Checkpoint**: AC-3 — the drop destination is obvious.

---

## Phase 6: User Story 4 — Move to the root (Priority: P2)

**Goal**: Dropping onto the root zone clears the document's folder.

**Independent test**: `PATCH …/folder {folderId:null}` sets `folder_id = NULL`; the root zone drop calls
`moveDocument(id, null)`.

- [X] T013 [US4] Integration + frontend: extend `MoveDocumentEndpointTests` — a move with `folderId:null` sets the document to the root; add a **root drop-zone** (`cdkDropList`) in `document-tree` whose drop calls `moveDocument(id, null)` + a spec — in `tests/RagBook.Api.IntegrationTests/Documents/MoveDocumentEndpointTests.cs` and `src/Web/src/app/documents/tree/document-tree.*`.

**Checkpoint**: AC-4 — documents can return to the top level.

---

## Phase 7: User Story 5 — Menu fallback (Priority: P1)

**Goal**: "Przenieś do…" moves a document without drag-and-drop, via the same action.

**Independent test**: The menu lists folders + root; choosing one calls `moveDocument` identically.

- [X] T014 [US5] Frontend: a per-document "Przenieś do…" menu (design-system, keyboard-reachable) listing the session's folders + a "Root" option → calls the same `TreeStore.moveDocument` — in `src/Web/src/app/documents/tree/document-row.*` (+ `document-tree` wiring) + a spec asserting a menu choice calls `moveDocument` with the chosen target (parity with drop).

**Checkpoint**: AC-5 — drag-and-drop is not the only path.

---

## Phase 8: Polish

- [X] T015 [US5] Integration: cross-session isolation — a `PATCH …/folder` on another session's document → 404 `document.not_found`; a target folder owned by another session → 404 `folder.not_found` — in `tests/RagBook.Api.IntegrationTests/Documents/MoveDocumentEndpointTests.cs`.
- [X] T016 [P] Docs: README **"Przenoszenie plików (drag & drop)"** — the move is a folder-attribute change (no re-index), the **optimistic-update-with-rollback** interaction, and the "Przenieś do…" accessibility fallback; AGENTS notes (`Documents/MoveDocument` slice + `PATCH /api/documents/{id}/folder`; `IDocumentMoveRepository`; `document.read_only`; `IFolderReference` reused; `TreeStore.moveDocument` optimistic+rollback + `@angular/cdk/drag-drop`; menu parity).
- [X] T017 Full green run — `npm test` in `src/Web` and `dotnet test` (Domain + Application + Testcontainers Integration; Docker up) — then the critical-analysis pass on the diff before opening the PR (repo memory: critical-analysis-before-pr). Then PR to master.

---

## Dependencies & execution order

- **Setup (T001)** + **Foundational (T002–T004)** block the stories.
- **US1 (T005–T010)** is the MVP: move slice + endpoint + optimistic store + drag-drop. **US2 (T011)** adds
  rollback; **US3 (T012)** target feedback; **US4 (T013)** move-to-root; **US5 (T014)** the menu fallback.
- Within a story, tests precede implementation; `[P]` = different files.
- Polish (T015–T017): isolation test + docs + green run.

## MVP scope

**US1 (T001–T010)** delivers the demonstrable increment: drag a document onto a folder and see it move instantly,
persisted, chunks untouched. US2–US5 add rollback, target feedback, move-to-root, and the menu fallback.
