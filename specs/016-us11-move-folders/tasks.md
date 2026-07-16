# Tasks: Przenoszenie folderów — z poddrzewem (US-11)

**Input**: Design documents from `specs/016-us11-move-folders/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/move-folder.md, quickstart.md

**Tests**: Included — Test-First (Constitution §IV). Domain (cycle/depth math), Application (handler branches),
Integration (Testcontainers: subtree-path rewrite / cycle / depth / duplicate / isolation / docs-untouched /
chat-scope), Angular (optimistic re-nest + rollback, enter-predicate, `onDrop` routing, menu).

**Organization**: A backend transactional move slice + extending the US-10 DnD to folders. US1 = move-with-subtree
🎯 MVP; US2 = no-cycles; US3 = depth/name guards; US4 = move-to-root; US5 = optimistic/rollback; US-menu = "Przenieś
do…" fallback.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: parallelizable (different files, no ordering dependency).
- Paths: `src/RagBook/Modules/Folders`, `src/RagBook.Infrastructure`, `src/RagBook.API/Endpoints`, `src/Web/src/app/{core,documents}`, `tests/…`.

---

## Phase 1: Setup

- [X] T001 [P] Add `FolderErrors.CircularMove` (`folder.circular_move`, `Error.Conflict` → 409) in `src/RagBook/Modules/Folders/Errors/FolderErrors.cs`.

---

## Phase 2: Foundational — move repository (transactional, session-scoped)

- [X] T002 `IFolderMoveRepository` (`GetByIdAsync`, `MaxSubtreeDepthAsync(pathPrefix)`, `SiblingExistsAsync(parentId, name)` case-insensitive, `MoveAsync(movedId, newParentId, oldPrefix, newPrefix)`) in `src/RagBook/Modules/Folders/Domain/` + `FolderMoveRepository` (EF; the reads use the session filter; `MoveAsync` runs **one transaction** — bulk `UPDATE folders SET path=@newPrefix||substr(path,len(@oldPrefix)+1) WHERE path LIKE @oldPrefix||'%' AND user_session_id=@session` then `UPDATE parent_id` of the moved row) in `src/RagBook.Infrastructure/SharedContext/Persistence/FolderMoveRepository.cs` + DI registration. **The bulk UPDATE MUST filter `user_session_id`** (raw SQL bypasses the global filter).

**Checkpoint**: a folder + subtree can be re-parented + path-rewritten atomically, session-scoped.

---

## Phase 3: User Story 1 — Move a folder with its subtree (Priority: P1) 🎯 MVP

**Goal**: A folder and everything inside it moves to the target in one operation; every descendant's path updates.

**Independent test**: `PATCH /api/folders/{id}/parent` moves `Umowy/2026` into `Archiwum` → `Archiwum/Umowy/2026`, all descendant paths rewritten, documents untouched.

- [X] T003 [US1] Domain test: `FolderMoveTests` — `FolderPath.IsPrefixOf` is the cycle primitive: a path is an ancestor-or-self prefix of a descendant (→ true, i.e. self-move and descendant-move both caught), and unrelated/target-ancestor paths are not (→ false) — in `tests/RagBook.Domain.Tests/Folders/FolderMoveTests.cs`. (Subtree-depth math lives in the handler and is Application-tested in T012.)
- [X] T004 [US1] Application test (Red): `MoveFolderHandlerTests` — a valid move calls `MoveAsync`; folder/target not in session → `folder.not_found`; move to the current parent → success with **no** `MoveAsync` (no-op) — in `tests/RagBook.Application.Tests/Folders/MoveFolderHandlerTests.cs`.
- [X] T005 [US1] `MoveFolderCommand(Guid FolderId, Guid? TargetParentId) : ICommand` + `MoveFolderCommandHandler(IFolderMoveRepository, IOptions<FolderOptions>)` implementing the D4 order (Green) — in `src/RagBook/Modules/Folders/Features/MoveFolder/*`.
- [X] T006 [US1] Endpoint `PATCH /api/folders/{id}/parent` (body `MoveFolderRequest(Guid? ParentId)`) → `Result` → 204 / ProblemDetails — in `src/RagBook.API/Endpoints/FolderEndpoints.cs`.
- [X] T007 [US1] Integration test: move `Umowy/2026` (files in both) into `Archiwum` → **every descendant folder's `path` is rewritten** to `Archiwum/Umowy/2026…`; the documents' `folder_id` is **unchanged**; a chat scope over `Archiwum` (US-13) **includes the moved documents** — in `tests/RagBook.Api.IntegrationTests/Folders/MoveFolderEndpointTests.cs`.
- [X] T008 [US1] Frontend store: `TreeStore.moveFolder(folderId, targetParentId: string \| null)` — no-op if same parent; **optimistically** set the moved folder's `parentId` (subtree re-nests via `buildForest`); `PATCH`; on success `refresh()` to correct `path`/`depth` — in `src/Web/src/app/core/tree.store.ts` + `tree.store.spec.ts` (optimistic re-parent applied; no-op issues no request).
- [X] T009 [US1] Frontend drag-drop: folder rows become `cdkDrag` (`[cdkDragData]="node"`); `onDrop` branches on `node.kind` → `moveFolder` / `moveDocument` — in `src/Web/src/app/documents/tree/document-tree.*` + a `document-tree.spec.ts` case that dropping a folder onto a folder calls `moveFolder` with that target.

**Checkpoint**: AC-1 — a folder + subtree moves, instantly and persisted, docs untouched, chat scope follows. MVP.

---

## Phase 4: User Story 2 — Cycles are impossible (Priority: P1)

**Goal**: A folder can't move into itself or a descendant — not offered during the drag, refused at the API.

**Independent test**: Move `A` into `A/B` → `folder.circular_move`; the drag never highlights a descendant.

- [X] T010 [US2] Application + integration: the handler returns `folder.circular_move` when `moved.IsPrefixOf(target)` (add the assertion to `MoveFolderHandlerTests` + an integration case) — in `tests/RagBook.Application.Tests/Folders/MoveFolderHandlerTests.cs` + `tests/RagBook.Api.IntegrationTests/Folders/MoveFolderEndpointTests.cs`.
- [X] T011 [US2] Frontend: `TreeStore.isDescendant(targetId, movedId)` (walks the `parentId` chain); a `cdkDropListEnterPredicate` on folder targets rejects the moved folder + its descendants (a descendant never highlights) — in `src/Web/src/app/core/tree.store.ts` + `src/Web/src/app/documents/tree/document-tree.*` + specs (`isDescendant` truth table; predicate rejects a subtree target).

**Checkpoint**: AC-2 — cycles impossible in the gesture and the API.

---

## Phase 5: User Story 3 — Depth & name-conflict guards (Priority: P2)

**Goal**: A too-deep or same-name move is refused with the right reason.

**Independent test**: A move exceeding max depth → `max_depth_exceeded`; a same-named target folder → `duplicate_name`.

- [X] T012 [US3] Application + integration: the handler returns `folder.max_depth_exceeded` when `targetDepth + subtreeHeight + 1 > MaxDepth`, and `folder.duplicate_name` when a same-named sibling exists in the target (`SiblingExistsAsync`) — in `MoveFolderHandlerTests` + `MoveFolderEndpointTests`.

**Checkpoint**: AC-3/AC-4 — the tree invariants hold.

---

## Phase 6: User Story 4 — Move to the root (Priority: P2)

**Goal**: Dropping a folder onto the root zone makes it (and its subtree) top-level.

**Independent test**: `PATCH …/parent {parentId:null}` → the folder becomes root; descendants' paths rewritten.

- [X] T013 [US4] Integration + frontend: extend `MoveFolderEndpointTests` — `parentId:null` re-roots the folder and rewrites descendant paths; the existing root drop-zone routes a **folder** drop to `moveFolder(id, null)` — in `tests/RagBook.Api.IntegrationTests/Folders/MoveFolderEndpointTests.cs` + `src/Web/src/app/documents/tree/document-tree.*`.

**Checkpoint**: AC — a branch can be promoted to top-level.

---

## Phase 7: User Story 5 — Optimistic + rollback (Priority: P1)

**Goal**: The move re-nests instantly; a rejected move snaps back with a reason.

**Independent test**: A failed folder `PATCH` reverts the `parentId` and shows a notice.

- [X] T014 [US5] Frontend test + impl: `TreeStore.moveFolder` **reverts** the moved folder's `parentId` on a failed `PATCH` and sets `moveError` (reuse the US-10 code→message map + `folder.circular_move`/`max_depth_exceeded`/`duplicate_name`); `document-tree` renders the notice — in `src/Web/src/app/core/tree.store.ts` (+ spec: failed folder move → original parent restored + error) and `document-tree.*`.

**Checkpoint**: AC-5 — optimism is safe for folders too.

---

## Phase 8: User Story — "Przenieś do…" menu fallback (Priority: P1)

**Goal**: A folder can be moved without drag-and-drop, via the same action (a11y parity — clarify).

**Independent test**: The folder menu lists valid targets (excluding self + subtree) + Root; choosing one calls `moveFolder`.

- [X] T015 [US-menu] Frontend: a per-folder "Przenieś do…" menu listing target folders filtered by `id !== moved && !isDescendant(id, moved)` (excludes the folder **itself** + its subtree) + a "Root" option → calls the same `TreeStore.moveFolder` — in `src/Web/src/app/documents/tree/document-tree.*` + a spec asserting a menu choice calls `moveFolder` and that neither the folder itself nor a descendant is offered.

**Checkpoint**: FR-011 — folder moves have a non-pointer path.

---

## Phase 9: Polish

- [X] T016 [P] Integration: cross-session isolation — `PATCH …/parent` on another session's folder → 404 `folder.not_found`; a target folder owned by another session → 404; assert the bulk path `UPDATE` never touches another session's rows — in `tests/RagBook.Api.IntegrationTests/Folders/MoveFolderEndpointTests.cs`.
- [X] T017 [P] Docs: README **"Przenoszenie poddrzewa jednym UPDATE"** — the transactional re-parent + single session-scoped path-prefix `UPDATE` (documents untouched, chat scope follows), cycle via materialized-path prefix, optimistic/rollback; AGENTS notes (`Folders/MoveFolder` + `PATCH /api/folders/{id}/parent`; `IFolderMoveRepository` raw-SQL bulk update **session-scoped**; `folder.circular_move`; `TreeStore.moveFolder` + `isDescendant` + `onDrop` routing + folder menu).
- [X] T018 Full green run — `npm test` in `src/Web` and `dotnet test` (Domain + Application + Testcontainers Integration; Docker up) — then the critical-analysis pass on the diff before opening the PR (repo memory: critical-analysis-before-pr). Then PR to master.

---

## Dependencies & execution order

- **Setup (T001)** + **Foundational (T002)** block the stories.
- **US1 (T003–T009)** is the MVP: move repo + handler + endpoint + optimistic store + folder drag. **US2 (T010–T011)**
  no-cycles; **US3 (T012)** depth/name; **US4 (T013)** move-to-root; **US5 (T014)** rollback; **menu (T015)**.
- Within a story, tests precede implementation; `[P]` = different files.
- Polish (T016–T018): isolation + docs + green run.

## MVP scope

**US1 (T001–T009)** delivers the demonstrable increment: drag a folder onto another and its whole subtree moves
instantly, persisted, with descendant paths rewritten and documents untouched. US2–US5 + the menu add the guards,
move-to-root, rollback, and the non-pointer path.
