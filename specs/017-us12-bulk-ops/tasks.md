# Tasks: Operacje zbiorcze na plikach — bulk move / delete (US-12)

**Input**: Design documents from `specs/017-us12-bulk-ops/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/bulk-operations.md, quickstart.md

**Tests**: REQUIRED (constitution §IV Test-First; user's standing rule — all 4 tiers green before any PR).

**Organization**: Grouped by user story. The five stories are all P1 and share one backend core (bulk validate-all
→ apply); the shared backend lives in **Foundational** so each story phase stays thin and independently testable.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: different file, no dependency on an incomplete task → parallelizable.
- **[Story]**: US1 (select + action bar), US2 (bulk move), US3 (bulk delete), US4 (all-or-nothing), US5 (per-id ownership).

---

## Phase 1: Setup

- [x] T001 Confirm branch `fm/us12-bulk-ops` and that master (US-11) is merged in; no new packages needed (reuses Wolverine, EF, CDK).

---

## Phase 2: Foundational (Blocking Prerequisites)

**⚠️ The shared bulk core. All user stories depend on this.**

### Domain / contracts

- [x] T002 [P] Add `BulkFailure` record `(Guid Id, string Code)` in `src/RagBook/Modules/Documents/Domain/BulkFailure.cs`.
- [x] T003 [P] Add `BulkResult` (`Success()` / `BadRequest(Error)` / `ValidationFailed(IReadOnlyList<BulkFailure>)`) in `src/RagBook/Modules/Documents/Domain/BulkResult.cs`.
- [x] T004 [P] Add `BulkOptions { int MaxItems = 50 }` (SectionName `"Bulk"`) in `src/RagBook/Modules/Documents/BulkOptions.cs`; bind it in `Program.cs`.
- [x] T005 Extend `DocumentErrors` in `src/RagBook/Modules/Documents/Errors/DocumentErrors.cs`: const `BulkValidationFailedCode = "document.bulk_validation_failed"`; `BulkEmpty` (`document.bulk_empty`, Validation) and `BulkTooLarge` (`document.bulk_too_large`, Validation). Reuse existing `NotFound`/`ReadOnly`/`TargetFolderNotFound` codes for `failures[]`.
- [x] T006 [P] Define `IDocumentBulkRepository` (`GetByIdsAsync(IReadOnlyCollection<Guid>)`, `MoveAllAsync(docs, Guid? targetFolderId)`, `DeleteAllAsync(docs)`) in `src/RagBook/Modules/Documents/Domain/IDocumentBulkRepository.cs`.

### Infrastructure

- [x] T007 Implement `DocumentBulkRepository` in `src/RagBook.Infrastructure/SharedContext/Persistence/DocumentBulkRepository.cs`: session-filtered `GetByIdsAsync` (`WHERE id = ANY`); `MoveAllAsync` (set each `FolderId`, one `SaveChanges`); `DeleteAllAsync` (one transaction, chunk FK cascade, commit, then best-effort blob per doc — reuse the US-08 pattern). Register in DI.

### API plumbing

- [x] T008 [P] Add `BulkProblemResults.ValidationFailed(IReadOnlyList<BulkFailure>)` → `422` ProblemDetails with `code` + `failures: [{ id, code }]` + `traceId` in `src/RagBook.API/ProblemDetails/BulkProblemResults.cs`.

**Checkpoint**: shared bulk core compiles; endpoints + handlers can now be added per story.

---

## Phase 3: User Story 1 — Select files and see the action bar (P1) 🎯 MVP

**Goal**: Tick documents → action bar "N zaznaczonych: Przenieś do… | Usuń | Anuluj"; Anuluj clears.

**Independent Test**: Karma — toggle two ids → `count === 2`, `selectedIds` correct; clear empties it.

- [x] T009 [US1] Karma spec `selection.store.spec.ts` (FAIL first): `toggle`/`has`/`count`/`clear`/`selectedIds`; `selectRange(folderDocIds, from, to)` selects the contiguous slice.
- [x] T010 [US1] Implement `SelectionStore` in `src/Web/src/app/core/selection.store.ts`: `selected: Set<string>`, `failedIds: Set<string>`, `toggle`/`has`/`count`/`clear`/`selectedIds`/`selectRange`.
- [x] T011 [US1] Add a select checkbox to document leaf rows in `src/Web/src/app/documents/tree/document-tree.*` (+ `document-row` if separate); Shift-click → `selectRange` within the folder.
- [x] T012 [US1] Add the bulk action bar (shows while `count > 0`): "N zaznaczonych: Przenieś do… | Usuń | Anuluj"; "Anuluj" → `clear()`. Design tokens, ≥360px. Karma: bar visible + count text + Anuluj clears.

**Checkpoint**: selection + action bar work; Move/Delete wired in US2/US3.

---

## Phase 4: User Story 2 — Bulk move (P1)

**Goal**: `POST /api/documents/bulk-move { ids, targetFolderId }` moves all selected; tree updates.

**Independent Test**: Integration — 3 docs across folders → bulk-move → all in target; Application — all-valid applies, missing target folder → `folder.not_found` failure.

- [x] T013 [P] [US2] Application test `BulkMoveHandlerTests.cs` (FAIL first): all-valid → `Success` + `MoveAllAsync` called; one `not_found`/`read_only`/missing-target-folder → `ValidationFailed` + **no** `MoveAllAsync`; dedup; empty → `BulkEmpty`; over-cap → `BulkTooLarge`.
- [x] T014 [US2] `Documents/Features/BulkMove/` command + handler: dedup → empty/over-cap guard → `GetByIdsAsync` → build failures (`not_found` absent, `read_only` demo, `folder.not_found` via `IFolderReference` when target set & absent) → any failure ⇒ `ValidationFailed`, else `MoveAllAsync` ⇒ `Success`.
- [x] T015 [US2] `POST /api/documents/bulk-move` in `src/RagBook.API/.../DocumentEndpoints.cs` (+ `BulkMoveRequest { Guid[] Ids; Guid? TargetFolderId }`): map `BulkResult` → `204` / `400` (ProblemResults) / `422` (BulkProblemResults).
- [x] T016 [P] [US2] Integration test in `BulkOperationsEndpointTests.cs`: bulk-move 3 docs into a folder → `204` + all have the new `folder_id`.
- [x] T017 [US2] Frontend: `SelectionStore.bulkMove(targetFolderId)` POSTs ids; on `204` clear + refresh tree/quota; "Przenieś do…" → folder picker (reuse US-10 pattern, incl. Root). Karma: picker → `bulk-move` called with ids + target.

**Checkpoint**: bulk move works end-to-end.

---

## Phase 5: User Story 3 — Bulk delete (P1)

**Goal**: `POST /api/documents/bulk-delete { ids }` deletes records + chunks; quota −N; behind a design-system confirm.

**Independent Test**: Integration — 3 docs → bulk-delete → gone + chunks gone + quota −3; Application — one bad item rejects all with no delete.

- [x] T018 [P] [US3] Application test `BulkDeleteHandlerTests.cs` (FAIL first): all-valid → `Success` + `DeleteAllAsync`; one bad → `ValidationFailed` + **no** `DeleteAllAsync`; dedup; empty/over-cap guards.
- [x] T019 [US3] `Documents/Features/BulkDelete/` command + handler: dedup → guards → `GetByIdsAsync` → failures (`not_found`/`read_only`) → any failure ⇒ `ValidationFailed`, else `DeleteAllAsync` ⇒ `Success`.
- [x] T020 [US3] `POST /api/documents/bulk-delete` in `DocumentEndpoints.cs` (+ `BulkDeleteRequest { Guid[] Ids }`): map `BulkResult` → `204` / `400` / `422`.
- [x] T021 [P] [US3] Integration test in `BulkOperationsEndpointTests.cs`: bulk-delete 3 docs → `204`; rows + chunks gone (cascade); quota dropped by 3.
- [x] T022 [US3] Frontend: `SelectionStore.bulkDelete()`; "Usuń" opens a **design-system confirm** (count + names, never `window.confirm`) → POST → on `204` clear + refresh tree/quota. Karma: confirm → `bulk-delete` called; cancel → no call.

**Checkpoint**: bulk delete works end-to-end with quota drop.

---

## Phase 6: User Story 4 — All-or-nothing on failure (P1)

**Goal**: any invalid item ⇒ whole op refused (`422` + `failures[]`), nothing changed; UI marks the items.

**Independent Test**: Integration — a selection with a read-only demo doc (or missing target folder) → `422 document.bulk_validation_failed` + `failures[]`, **nothing** moved/deleted.

- [x] T023 [P] [US4] Integration test in `BulkOperationsEndpointTests.cs`: bulk-delete with a demo (read-only) doc in the set → `422`, `code = document.bulk_validation_failed`, `failures` names it with `document.read_only`, and **every** doc still present + quota unchanged; bulk-move with an absent `targetFolderId` → `422` + `folder.not_found`, nothing moved.
- [x] T024 [US4] Frontend: on `422`, `SelectionStore` reads `failures[]` → `failedIds`; the tree/action bar marks those rows. Karma: a stubbed `422` sets `failedIds` and marks the row; selection is **not** cleared.

**Checkpoint**: all-or-nothing proven at the integration tier + surfaced in the UI.

---

## Phase 7: User Story 5 — Per-id ownership validation (P1)

**Goal**: a foreign id is reported as `document.not_found` and fails the whole op (no existence disclosure).

**Independent Test**: Integration — bulk request with another session's document id → `422`, that id `document.not_found`, nothing changed; a second session's docs untouched.

- [x] T025 [P] [US5] Integration test in `BulkOperationsEndpointTests.cs`: seed a second session's doc; bulk-move/-delete including its id → `422` + `failures` reports it as `document.not_found`; the other session's doc is unchanged (isolation).

**Checkpoint**: session isolation for bulk endpoints proven.

---

## Phase 8: Polish & Cross-Cutting

- [x] T026 [P] Add the all-or-nothing-vs-partial-success trade-off note to `docs/features/README.md` (per DoD).
- [x] T027 [P] Update `docs/features/US-12-operacje-zbiorcze.md` status / cross-links if present.
- [x] T028 Run all 4 tiers green (Domain/Application/Integration-Testcontainers/Angular-Karma) per quickstart.md; then critical diff review before PR.

---

## Dependencies & Execution Order

- **Setup (T001)** → **Foundational (T002–T008)** blocks everything.
- **US1 (T009–T012)** frontend-only; can run in parallel with backend once Foundational is done.
- **US2 (T013–T017)** and **US3 (T018–T022)** depend on Foundational; each adds its own slice + endpoint + integration test + frontend wiring. The shared `BulkOperationsEndpointTests.cs` and `DocumentEndpoints.cs` are touched by both → sequence those edits (US2 before US3).
- **US4 (T023–T024)** and **US5 (T025)** are additional tests + a frontend marking behaviour over the US2/US3 handlers → after both handlers exist.
- **Polish (T026–T028)** last.

### Parallel Opportunities

- T002/T003/T004/T006/T008 (distinct new files) in parallel.
- Application tests T013 & T018 in parallel (distinct files).
- Integration tests share one file → sequence T016 → T021 → T023 → T025.

---

## Implementation Strategy

**MVP** = US1 + US2 + US3 (select, move, delete) with US4/US5 tests proving the safety contract. Backend core
(Foundational) first, then the two slices with their handlers/endpoints/tests, then the frontend wiring, then the
all-or-nothing + isolation integration tests, then the README trade-off note and the full green run + critical
review before the PR.
