# Tasks: Folder CRUD (Hierarchia folderów)

**Input**: Design documents from `specs/003-us09-folders/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/folders-api.md, quickstart.md

**Tests**: Included — the constitution mandates Test-First (Red→Green→Refactor); every behavior lands
via a failing test first, at the cheapest tier that proves it (Domain → Application → Integration).

**Organization**: US-09 builds on the existing US-01 foundation, so Setup is thin. The `Folder`
aggregate, path VO, error catalog, options, seams, and migration are Foundational (block every story).
Tasks are grouped by the spec's user stories: US1 = create/nest + list (AC-1), US2 = depth (AC-2), US3
= uniqueness + race (AC-3), US4 = rename (AC-4), US5 = delete empty/non-empty (AC-5). Name validation
(AC-6) is a domain rule shared by create/rename, built in Foundational and asserted per story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel (different files, no incomplete dependency).
- Paths follow plan.md (`src/RagBook/Modules/Folders`, `src/RagBook.API`, `src/RagBook.Infrastructure`, `src/Web`, `tests/…`).

---

## Phase 1: Setup (Folders module skeleton + config)

**Purpose**: Create the new module shell and wire config — no behavior yet.

- [X] T001 Create the `Folders` module folder skeleton under `src/RagBook/Modules/Folders/` (`Domain/`, `Errors/`, `Features/{CreateFolder,RenameFolder,DeleteFolder,ListFolders}/`), copying the `Documents` module's shape.
- [X] T002 [P] Add a `Folders` section to `src/RagBook.API/appsettings.json` (`MaxDepth: 3`, `MaxNameLength: 100`) — the only source of the limits (no magic numbers).

**Checkpoint**: Solution still builds; module folders and config exist.

---

## Phase 2: Foundational (domain + seams + persistence — BLOCKS all stories)

**Purpose**: The `Folder` aggregate, path VO, name-validation + depth rules, error catalog, options,
the repository + file-emptiness seams, and the migration. Every AC depends on these.

### Domain (Red → Green)

- [X] T003 [P] Domain test (Red): `FolderPath` — `Should_HaveDepthEqualToSegmentCount`, `Should_AppendSegmentWithTrailingSlash`, `Should_DetectPrefixOfDescendant` in `tests/RagBook.Domain.Tests/Folders/FolderPathTests.cs`.
- [X] T004 [P] Domain test (Red): `Folder` create/name/depth — `Should_BuildRootPath_When_CreatingRoot` (AC-1), `Should_BuildChildPathUnderParent_When_CreatingChild` (AC-1), `Should_ReturnMaxDepthExceeded_When_ParentAtMaxDepth` (AC-2), `Should_ReturnInvalidName_When_NameEmptyAfterTrimOrTooLongOrHasSlash` (AC-6), `Should_TrimName_When_Creating` — in `tests/RagBook.Domain.Tests/Folders/FolderTests.cs`.
- [X] T005 [P] Domain test (Red): `Folder` rename — `Should_ChangeNameOnly_When_Renaming` (AC-4, path/parent unchanged), `Should_Succeed_When_RenamingToCurrentName` (no-op), `Should_ReturnInvalidName_When_RenamingToInvalidName` — in `tests/RagBook.Domain.Tests/Folders/FolderTests.cs`.
- [X] T006 Implement `FolderPath` value object (`Segments`, `Depth`, `Append(Guid)`, `IsPrefixOf`, `Contains`) in `src/RagBook/Modules/Folders/Domain/FolderPath.cs` (Green for T003).
- [X] T007 Implement the `Folder` aggregate (`ISessionOwned` + `IAuditable`; `Id`, `Name`, `ParentId`, `Path`; `CreateRoot`/`CreateChild`/`Rename` → `Result` with trim+length+`/` validation via `FolderNameRules`, depth guard from `maxDepth`) in `src/RagBook/Modules/Folders/Domain/Folder.cs` (Green for T004, T005).

### Errors & config (Green)

- [X] T008 [P] Implement `FolderErrors` catalog (`folder.invalid_name` Validation, `folder.max_depth_exceeded` Validation, `folder.duplicate_name` Conflict, `folder.not_empty` Conflict, `folder.not_found` NotFound, `folder.conflict` Conflict) in `src/RagBook/Modules/Folders/Errors/FolderErrors.cs`.
- [X] T009 [P] Implement `FoldersExceptionHandler.TryMap` (persistence `UniqueViolation` → `folder.duplicate_name`; other conflicts → `folder.conflict`; else fall through) in `src/RagBook/Modules/Folders/Errors/FoldersExceptionHandler.cs`, mirroring `DocumentsExceptionHandler`.
- [X] T010 [P] Implement `FolderOptions` (`MaxDepth=3`, `MaxNameLength=100`, `SectionName="Folders"`) + a `FolderNameRules` record derived from it, in `src/RagBook/Modules/Folders/FolderOptions.cs`; bind it in `src/RagBook.API/Program.cs` (`Configure<FolderOptions>(...)`).

### Seams (abstractions)

- [X] T011 Define `IFolderRepository` (`AddAsync`, `GetByIdAsync` session-filtered, `GetParentAsync`, `HasChildrenAsync`, `ListForSessionAsync` ordered by `LOWER(name)`, `Remove`) and `IFolderFileProbe` (`HasFilesAsync(folderId, ct)`) in `src/RagBook/Modules/Folders/Domain/`.

### Persistence

- [X] T012 Add `DbSet<Folder>` to `RagBookDbContext` and `FolderConfiguration` (table `folders`, `parent_id` nullable self-FK no-cascade, `path text`, audit columns, `ix_folders_user_session_id`) in `src/RagBook.Infrastructure/SharedContext/Persistence/`.
- [X] T013 Implement `FolderRepository` (session-scoped via the global query filter; `GetByIdAsync` returns `null` cross-session; `HasChildrenAsync` = `EXISTS(parent_id = id)`; `ListForSessionAsync` `ORDER BY LOWER(name)`) in `src/RagBook.Infrastructure/SharedContext/Persistence/FolderRepository.cs`; register `AddScoped<IFolderRepository, FolderRepository>()` in `src/RagBook.Infrastructure/DependencyInjection.cs`.
- [X] T014 [P] Implement `NoFolderFilesProbe : IFolderFileProbe` (returns `false` — no `documents.folder_id` until US-04) in `src/RagBook.Infrastructure/SharedContext/Persistence/NoFolderFilesProbe.cs`; register `AddScoped<IFolderFileProbe, NoFolderFilesProbe>()` in `src/RagBook.Infrastructure/DependencyInjection.cs`.
- [X] T015 Create migration `AddFolders` in `src/RagBook.Infrastructure.Migrations` — `folders` table + `ix_folders_user_session_id`, plus raw-SQL `ux_folders_root_name` (`UNIQUE (user_session_id, LOWER(name)) WHERE parent_id IS NULL`), `ux_folders_child_name` (`UNIQUE (user_session_id, parent_id, LOWER(name)) WHERE parent_id IS NOT NULL`), and `ix_folders_path` (`path text_pattern_ops`); applied via bundle/fixture, never at startup.

**Checkpoint**: Domain green; schema + uniqueness indexes exist; repository + probe wired.

---

## Phase 3: User Story 1 — Create folders and nest them (AC-1) 🎯 MVP

**Goal**: Create a root folder and a child; `GET /api/folders` shows the hierarchy, ordered.

**Independent test**: create "Umowy" (root) then "2026" (child) → `GET /api/folders` lists both with the child under the parent, ordered by name.

- [X] T016 [P] [US1] Application test (Red): `CreateFolderCommandHandler` — `Should_CreateRoot_When_ParentIdNull`, `Should_CreateChildUnderParent_When_ParentIdGiven`, `Should_ReturnNotFound_When_ParentInAnotherSession` (mocked `IFolderRepository` + `IOptions<FolderOptions>`, factory-method SUT) in `tests/RagBook.Application.Tests/Folders/CreateFolderCommandHandlerTests.cs`.
- [X] T017 [US1] Implement `CreateFolderCommand(Name, ParentId) : ICommand<Guid>`, its FluentValidation validator (name present), and `CreateFolderCommandHandler` (loads parent via `GetParentAsync` when `ParentId` set → `folder.not_found` if missing; `Folder.CreateRoot`/`CreateChild` with `FolderOptions`; `AddAsync`) in `src/RagBook/Modules/Folders/Features/CreateFolder/` (Green for T016).
- [X] T018 [P] [US1] Application test (Red): `ListFoldersQueryHandler` returns `FolderNode`s ordered by `LOWER(name)` (mocked repo) in `tests/RagBook.Application.Tests/Folders/ListFoldersQueryHandlerTests.cs`.
- [X] T019 [US1] Implement `ListFoldersQuery : IQuery<IReadOnlyList<FolderNode>>`, `FolderNode` (`Id`, `ParentId`, `Name`, `Depth`), and `ListFoldersQueryHandler` (calls `ListForSessionAsync`) in `src/RagBook/Modules/Folders/Features/ListFolders/` (Green for T018).
- [X] T020 [US1] Implement `FolderEndpoints` (`POST /api/folders` → 201 `{id}`, `GET /api/folders` → 200 ordered list) + `FolderContracts` DTOs in `src/RagBook.API/Endpoints/`; map `MapFolderEndpoints()` in `Program.cs`.
- [X] T021 [US1] Integration test (Red→Green): `Should_PersistHierarchy_When_CreatingRootThenChild` (create root + child via the API, assert `GET /api/folders` shows child under parent with correct depth) and `Should_Return404_When_OperatingOnAnotherSessionsFolder` (FR-010) in `tests/RagBook.Api.IntegrationTests/Folders/FolderEndpointTests.cs`.
- [X] T022 [P] [US1] Angular `FolderTreeStore` (signals, `providedIn: 'root'`: `tree` built from `parentId`, `refresh()` → `GET /api/folders`, `create()` → `POST /api/folders`) in `src/Web/src/app/core/folder-tree.store.ts` (+ unit test with `HttpTestingController`).
- [X] T023 [P] [US1] Angular folder-tree component (standalone, OnPush, signals, `@for`/`@if`) rendering the nested tree with a "New folder" context action (inline input) using design tokens (no inline hex) in `src/Web/src/app/folders/folder-tree.{ts,html,scss}` (+ unit test asserting nesting + create call); render it in `app.html`.

**Checkpoint**: AC-1 demonstrable — create root + child, hierarchy visible and ordered. MVP.

---

## Phase 4: User Story 2 — Depth limit enforced (AC-2)

**Goal**: Creating a child of a depth-3 folder is rejected; the UI hides "New folder" at max depth.

**Independent test**: seed a 3-level chain, `POST` a child of the deepest → 400 `folder.max_depth_exceeded`, nothing persisted.

- [X] T024 [US2] Integration test (Red→Green): `Should_RejectChild_When_ParentAtMaxDepth` — build a depth-3 chain via the API, attempt a 4th level, assert 400 `folder.max_depth_exceeded` and folder count unchanged — in `tests/RagBook.Api.IntegrationTests/Folders/FolderEndpointTests.cs`.
- [X] T025 [P] [US2] Angular: the folder-tree hides the "New folder" action when `node.depth >= MaxDepth` (FR-012); extend `folder-tree` + its unit test (depth-3 node offers no create action).

**Checkpoint**: AC-2 enforced server-side (domain guard from T007) and reflected in the UI.

---

## Phase 5: User Story 3 — Names unique within a parent (AC-3)

**Goal**: A duplicate name in the same parent is rejected (case-insensitive); the same name under a different parent succeeds; the guarantee holds under concurrency.

**Independent test**: create "Umowy" at root, `POST` "umowy" at root → 409 `folder.duplicate_name`; `POST` "Umowy" under another parent → 201.

- [X] T026 [US3] Integration test (Red→Green): `Should_RejectDuplicate_When_SameNameInSameParentCaseInsensitive`, `Should_AllowSameNameUnderDifferentParent`, and `Should_TreatTrimmedAndCaseFoldedNameAsDuplicate` (create "Umowy", then `POST` `"  umowy "` at the same parent → 409 `folder.duplicate_name`, proving trim+case-fold collide — spec Edge Cases / SC-006) in `tests/RagBook.Api.IntegrationTests/Folders/FolderEndpointTests.cs` — proves the two partial `LOWER(name)` indexes and the `23505`→`folder.duplicate_name` mapping (T009, T015).
- [X] T027 [US3] Integration test (Red): `Should_AdmitAtMostOne_When_TwoIdenticalCreatesRaceInSameParent` — fire two identical `CreateFolderCommand`s concurrently (independent `DbContext`s, same session) via `Task.WhenAll`, assert exactly one success and the other maps to `folder.duplicate_name`, final count == 1 — in `tests/RagBook.Api.IntegrationTests/Folders/FolderConcurrencyTests.cs`.
- [X] T028 [US3] Verify/adjust the create handler + `FoldersExceptionHandler` so the concurrent loser surfaces `folder.duplicate_name` (not a naked 500) — tune until T027 is reliably green across repeated runs (Green).

**Checkpoint**: AC-3 proven at root and nested, case-insensitively, and under real concurrency.

---

## Phase 6: User Story 4 — Rename a folder (AC-4)

**Goal**: Renaming changes only the name; the folder keeps its place and descendants are untouched; uniqueness + validation apply to the new name.

**Independent test**: rename a non-empty folder → new name shown, children keep their `path`/position; renaming to a sibling's name → 409.

- [X] T029 [P] [US4] Application test (Red): `RenameFolderCommandHandler` — `Should_Rename_When_NameValidAndUnique`, `Should_ReturnNotFound_When_FolderMissing`, `Should_ReturnInvalidName_When_NewNameInvalid` (mocked repo) in `tests/RagBook.Application.Tests/Folders/RenameFolderCommandHandlerTests.cs`.
- [X] T030 [US4] Implement `RenameFolderCommand(Id, NewName) : ICommand`, its validator, and `RenameFolderCommandHandler` (`GetByIdAsync` → `folder.not_found`; `Folder.Rename` with `FolderNameRules`; save) in `src/RagBook/Modules/Folders/Features/RenameFolder/` (Green for T029).
- [X] T031 [US4] Implement `PUT /api/folders/{id}/name` in `FolderEndpoints` (→ 204) and its request DTO in `FolderContracts`.
- [X] T032 [US4] Integration test (Red→Green): `Should_LeaveDescendantsInPlace_When_RenamingNonEmptyFolder` (rename a parent, assert children keep `path`/`parent_id`) and `Should_RejectRename_When_SiblingNameExists` (409 `folder.duplicate_name`) in `tests/RagBook.Api.IntegrationTests/Folders/FolderEndpointTests.cs`.
- [X] T033 [P] [US4] Angular: inline rename action on a tree node (double-click / context menu) calling `FolderTreeStore.rename()` → `PUT /api/folders/{id}/name`; extend `folder-tree` + `FolderTreeStore` + unit tests.

**Checkpoint**: AC-4 validated — rename is O(1) and descendants are undisturbed.

---

## Phase 7: User Story 5 — Delete empty, keep non-empty (AC-5)

**Goal**: An empty folder deletes after confirmation; a folder with subfolders/files is blocked with guidance.

**Independent test**: delete an empty folder → 204; attempt to delete a folder with a subfolder → 409 `folder.not_empty`, nothing removed.

- [X] T034 [P] [US5] Application test (Red): `DeleteFolderCommandHandler` — `Should_Delete_When_Empty`, `Should_ReturnNotEmpty_When_HasChild` (fake `IFolderRepository.HasChildrenAsync` true), `Should_ReturnNotEmpty_When_HasFiles` (fake `IFolderFileProbe` returns true), `Should_ReturnNotFound_When_Missing` in `tests/RagBook.Application.Tests/Folders/DeleteFolderCommandHandlerTests.cs`.
- [X] T035 [US5] Implement `DeleteFolderCommand(Id) : ICommand` and `DeleteFolderCommandHandler` (`GetByIdAsync` → `folder.not_found`; `HasChildrenAsync` || `IFolderFileProbe.HasFilesAsync` → `folder.not_empty`; else `Remove`, all in one transaction — FR-009) in `src/RagBook/Modules/Folders/Features/DeleteFolder/` (Green for T034).
- [X] T036 [US5] Implement `DELETE /api/folders/{id}` in `FolderEndpoints` (→ 204 / 409 / 404).
- [X] T037 [US5] Integration test (Red→Green): `Should_DeleteEmpty_And_BlockNonEmpty` — delete an empty folder (204), then attempt to delete a folder that has a subfolder (409 `folder.not_empty`, subtree intact) — in `tests/RagBook.Api.IntegrationTests/Folders/FolderEndpointTests.cs`.
- [X] T038 [P] [US5] Angular: delete action on a tree node using the shared confirm dialog (never `window.confirm`) → `FolderTreeStore.delete()` → `DELETE /api/folders/{id}`; surface `folder.not_empty` as a "Usuń lub przenieś zawartość" message; extend `folder-tree` + unit test.

**Checkpoint**: AC-5 validated — empty deletes, non-empty blocked; the file arm is seam-ready for US-04.

---

## Phase 8: Docs & polish (cross-cutting)

- [X] T039 Add a "Hierarchia folderów: materialized path" section to `README.md` — the `/A/B/C/` format with IDs as segments, the prefix subtree query (`path LIKE prefix || '%'` with `text_pattern_ops`, no recursive CTEs), the max-depth-by-segment rule, case-insensitive per-parent uniqueness, and rename-O(1) — per the Definition of Done.
- [X] T040 Record durable knowledge in `AGENTS.md` (Folders module + materialized path; two partial `LOWER(name)` unique indexes and why root needs its own; `IFolderFileProbe` is a no-op until US-04 wires `documents.folder_id`; rename never re-paths descendants).
- [X] T041 Full green run: `dotnet test RagBook.slnx` (Domain + Application + Testcontainers Integration) and `npm test` in `src/Web`; verify `dotnet run --project src/RagBook.AppHost` starts clean and the folder tree renders create/rename/delete.

---

## Dependencies & execution order

- **Setup (T001–T002)** → **Foundational (T003–T015)** block every story.
- **US1 (T016–T023)** is the MVP (create + list + tree UI). **US2 (T024–T025)** reuses the Foundational
  depth guard. **US3 (T026–T028)** relies on the migration's unique indexes + exception handler. **US4
  (T029–T033)** and **US5 (T034–T038)** add the rename and delete slices on the same aggregate/repo.
- Within a phase, `[P]` tasks touch different files and may run in parallel. Test tasks precede their
  implementation (Red→Green→Refactor); the concurrency test (T027) and delete integration (T037) come
  after their handlers exist.
- Polish (T039–T041) after all stories are green.

## Parallel example (Foundational)

T003, T004, T005, T008, T009, T010 (`[P]`) touch independent files and can run together; T006/T007
(domain implementations) follow their Red tests; T011–T015 (seams + persistence) follow the aggregate.

## MVP scope

**US1 (T001–T023)** yields a demonstrable increment: create a root folder and a nested folder and see
the ordered hierarchy in the tree via `GET /api/folders`. US2–US5 complete depth enforcement,
case-insensitive uniqueness (incl. the concurrency guarantee), rename, and empty-only delete required
by the Definition of Done.
