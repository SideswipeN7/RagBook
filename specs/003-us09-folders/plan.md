# Implementation Plan: Folder CRUD (Hierarchia folderów)

**Branch**: `003-us09-folders` (spec dir `003-us09-folders`) | **Date**: 2026-07-10 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/003-us09-folders/spec.md`

## Summary

US-09 introduces a **`Folders`** vertical-slice module: a session-owned folder tree the visitor can
**create**, **rename**, and **delete**, nested up to **3 levels**, with names **unique
(case-insensitive) within a parent** and only **empty** folders deletable. Hierarchy is a
**materialized path whose segments are folder IDs** (`/A/B/C/`), so a subtree is a prefix match
(`path LIKE parent.path || '%'`, `text_pattern_ops` index) with **no recursive CTEs**, and a rename is
**O(1)** (segments are IDs, not names, so descendants are untouched). Limits (max depth 3, max name
length 100) come from **`FolderOptions`** via `IOptions<T>` — zero magic numbers.

Technical approach: the depth rule, path construction, and name normalization (trim → validate length
/ reject `/`) live in the **`Folder` aggregate** as pure `Result`-returning factories
(`CreateRoot`/`CreateChild`/`Rename`), so AC-1/AC-2/AC-6 are domain-tested cheaply. Uniqueness is
enforced authoritatively by **two partial unique indexes** on `(user_session_id, [parent_id,]
LOWER(name))` — one for root (`parent_id IS NULL`), one for nested — so the concurrent-duplicate race
(AC-3 / FR-005) is caught by the database and mapped back to `folder.duplicate_name` by a new
**`FoldersExceptionHandler`** (Postgres `23505` → code), reusing the existing
`NpgsqlPersistenceExceptionClassifier`. Delete emptiness checks direct children (`parent_id = X`) now
and files through a **forward-looking seam** (`IFolderFileProbe`, returns "no files" until US-04 adds
`documents.folder_id`). Sibling ordering (FR-013) is `ORDER BY LOWER(name)` in the list read. The
frontend adds a signals `FolderTreeStore` and context actions (new/rename/delete) on tree nodes, using
the shared confirm dialog — never native dialogs.

> **In-scope read decision (not escalated):** US-09 ships a **minimal `GET /api/folders`** returning the
> session's folders (ordered, with parent/path) so AC-1's "tree shows the hierarchy" and FR-013 are
> independently testable now. **US-07** later builds the richer tree-with-documents view on top of this
> same read — US-09 does not build document listing. Recorded in research.md D7.

## Technical Context

**Language/Version**: C# (LangVersion `preview`) on **.NET 10**; TypeScript (Angular latest stable)

**Primary Dependencies**: ASP.NET Core, **Wolverine** (in-process dispatch), EF Core + Npgsql,
FluentValidation, `Microsoft.Extensions.Options` (config binding), .NET Aspire, Angular standalone/signals

**Storage**: PostgreSQL — a new `folders` table (id, user_session_id + index, parent_id NULL self-FK,
name, path text). Two partial unique indexes on `LOWER(name)` (root vs nested) enforce
case-insensitive per-parent uniqueness and the AC-3 race; a `text_pattern_ops` index on `path`
supports prefix subtree queries (used by delete's subtree check and future US-13 scope).

**Testing**: xUnit + FluentAssertions across three tiers; **Testcontainers** PostgreSQL for the
integration tier (AC-1 hierarchy, AC-2 depth, AC-3 uniqueness + concurrent-duplicate race, AC-4 rename
leaves descendants, AC-5 delete empty/blocked non-empty, FR-013 ordering, cross-session 404); Angular
unit tests for the tree store/actions.

**Target Platform**: Linux container → GCP Cloud Run (stateless API); modern browsers for the SPA

**Project Type**: Web application (Angular SPA + ASP.NET Core modular monolith), Aspire-orchestrated

**Performance Goals**: Not a US-09 driver. Create/rename/delete are single-row writes; the folder list
is one indexed, session-filtered read ordered by `LOWER(name)`; subtree checks use the `path` prefix
index. Case-study scale.

**Constraints**: All limits config-driven (no magic numbers); errors via `Result<T>` → ProblemDetails
with a stable `code`, never a naked 500; isolation inherited from US-01's global query filter (a
cross-session folder id resolves as not-found → 404, never 403); rename must not re-path descendants;
only empty folders deletable; migrations applied out-of-band.

**Scale/Scope**: US-09 delivers create/rename/delete + a minimal folder-list read + the config + the
file-emptiness seam + the tree UI actions. Explicitly **not**: moving/reparenting folders (US-10/US-11),
cascade delete, the documents-in-tree view (US-07), folder colours/icons/favourites.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | US-09 compliance |
|---|---|
| **I. Vertical-slice modular monolith** | New `Folders` module (`Domain/`, `Errors/`, `Features/{CreateFolder,RenameFolder,DeleteFolder,ListFolders}`) beside `Session`/`Documents`; no cross-module references — the file-emptiness check is a seam owned by Folders, wired to Documents later via US-04 without a direct reference. ✅ |
| **II. CQRS + Result contract** | `CreateFolderCommand : ICommand<Guid>`, `RenameFolderCommand`/`DeleteFolderCommand : ICommand`, `ListFoldersQuery : IQuery<...>`; `FolderErrors` closed catalog (`folder.duplicate_name`, `folder.max_depth_exceeded`, `folder.not_empty`, `folder.invalid_name`, `folder.not_found`, `folder.conflict`); `FoldersExceptionHandler` maps `23505`→`duplicate_name` via the global ProblemDetails mapper. `Permissions/` deferred — see Complexity Tracking. ✅ (justified deviation) |
| **III. Data isolation by session** | `Folder` implements `ISessionOwned`; create/rename/delete/list all flow through the existing EF global query filter, so a folder id from another session materializes as `null` → `folder.not_found` → 404. ✅ |
| **IV. Test-first (Red→Green→Refactor)** | Domain (`Folder.CreateChild` depth + path build = AC-1/AC-2, `Rename`/name-normalization = AC-4/AC-6), Application (handlers with mocked repo, factory-method SUT), Integration (Testcontainers: AC-1 hierarchy, AC-2 depth, AC-3 uniqueness + concurrent race, AC-4 descendants intact, AC-5 empty/non-empty, FR-013 ordering, cross-session 404). ✅ |
| **V. Provider resilience + cache** | No external providers in US-09 — N/A. ✅ |
| **VI. Auditing & time** | `Folder` implements `IAuditable`; stamped by the existing `AuditingInterceptor` via `TimeProvider`; `UserSessionId` stamped by `SessionStampingInterceptor` — never by hand. ✅ |
| **VII. Secrets** | No secrets. `FolderOptions` bound from `Folders:*` (MaxDepth, MaxNameLength) — config-driven limits, zero magic numbers. ✅ |
| **VIII. Operations & delivery** | Migration `AddFolders` created in `RagBook.Infrastructure.Migrations`, applied via bundle/init/fixture — never at startup. AppHost/ServiceDefaults unchanged. ✅ |
| **IX. Frontend & design system** | Angular standalone folder-tree actions (OnPush, signals, new control flow) + `FolderTreeStore`; shared confirm dialog for delete (never `window.confirm`); "New folder" hidden at max depth; design tokens from `DESIGN.md`, no inline hex; 404 mapped by the existing interceptor. ✅ |

**Gate result: PASS** with one justified deviation (`Permissions/` folder deferred — anonymous sessions, no roles).

## Project Structure

### Documentation (this feature)

```text
specs/003-us09-folders/
├── plan.md              # This file
├── research.md          # Phase 0 — decisions & rationale (path format, uniqueness index, depth, delete seam, ordering)
├── data-model.md        # Phase 1 — Folder aggregate, FolderNode read model, FolderOptions, seams, DDL
├── quickstart.md        # Phase 1 — run & validate AC-1..AC-6
├── contracts/
│   └── folders-api.md   # Phase 1 — create/rename/delete/list endpoints + repository & file-probe seam contracts
├── checklists/
│   └── requirements.md  # spec quality checklist
└── tasks.md             # Phase 2 — /speckit-tasks output
```

### Source Code (repository root) — new/changed for US-09

```text
src/
├── RagBook/                                          # Core
│   └── Modules/
│       └── Folders/                                  # NEW module
│           ├── Domain/
│           │   ├── Folder.cs                          # ISessionOwned + IAuditable; CreateRoot/CreateChild/Rename → Result; path build, depth guard
│           │   ├── FolderPath.cs                      # value object: segments (IDs), Depth, Append(id), IsPrefixOf — pure
│           │   ├── IFolderRepository.cs               # AddAsync, GetByIdAsync (session-filtered), HasChildrenAsync, ListForSessionAsync, Remove
│           │   └── IFolderFileProbe.cs                # forward-looking seam: HasFilesAsync(folderId) — US-04 wires the real check
│           ├── Errors/
│           │   ├── FolderErrors.cs                    # duplicate_name, max_depth_exceeded, not_empty, invalid_name, not_found, conflict
│           │   └── FoldersExceptionHandler.cs         # Npgsql 23505 (unique) → folder.duplicate_name
│           ├── FolderOptions.cs                       # bound from Folders:* (MaxDepth=3, MaxNameLength=100)
│           └── Features/
│               ├── CreateFolder/                      # CreateFolderCommand : ICommand<Guid> + Handler + Validator
│               ├── RenameFolder/                      # RenameFolderCommand : ICommand + Handler + Validator
│               ├── DeleteFolder/                      # DeleteFolderCommand : ICommand + Handler
│               └── ListFolders/                       # ListFoldersQuery : IQuery<IReadOnlyList<FolderNode>> + Handler + FolderNode
├── RagBook.API/
│   ├── Program.cs                                     # + Configure<FolderOptions>(...); MapFolderEndpoints()
│   ├── appsettings.json                               # + "Folders": { "MaxDepth": 3, "MaxNameLength": 100 }
│   └── Endpoints/
│       ├── FolderEndpoints.cs                         # POST /api/folders, PUT /api/folders/{id}/name, DELETE /api/folders/{id}, GET /api/folders
│       └── FolderContracts.cs                         # request/response DTOs
├── RagBook.Infrastructure/
│   ├── DependencyInjection.cs                         # + AddScoped<IFolderRepository, FolderRepository>(); + IFolderFileProbe no-op impl
│   └── SharedContext/Persistence/
│       ├── RagBookDbContext.cs                        # + DbSet<Folder>
│       ├── Configurations/FolderConfiguration.cs      # table map, self-FK, session index, 2 partial unique lower(name) indexes, path text_pattern_ops index
│       ├── FolderRepository.cs                        # session-filtered reads/writes; HasChildren; ordered ListForSession
│       └── NoFolderFilesProbe.cs                      # IFolderFileProbe no-op (no documents.folder_id yet) — replaced in US-04
├── RagBook.Infrastructure.Migrations/Migrations/      # AddFolders (folders table + indexes)
└── Web/src/app/
    ├── core/folder-tree.store.ts                     # signals store: tree + create/rename/delete + refresh
    └── folders/                                       # folder-tree actions (context menu, inline rename), standalone OnPush
tests/
├── RagBook.Domain.Tests/Folders/                      # FolderTests (depth, path build, rename O(1)), FolderPathTests, name-validation
├── RagBook.Application.Tests/Folders/                 # Create/Rename/Delete/ListFolders handler tests (mocked repo + probe)
└── RagBook.Api.IntegrationTests/Folders/              # hierarchy (AC-1), depth (AC-2), uniqueness+race (AC-3), rename (AC-4), delete (AC-5), ordering (FR-013), cross-session 404
```

**Structure Decision**: `Folders` is a sibling module under `src/RagBook/Modules/`, copying the
`Documents`/`Session` module shape (aggregate + `Errors/` + per-module exception handler + `Features/`
slices). US-09 fills the module end-to-end for CRUD; US-10/US-11 later add move/reparent slices, and
US-07 adds the documents-in-tree read on top of `ListFolders`.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| `Folders` module ships **no `Permissions/` folder** (§II says every module owns one) | Sessions are anonymous with **no roles**; folder CRUD applies uniformly to the owning session — there is nothing to authorize beyond session ownership, which the query filter already enforces. | An empty `Permissions/` folder is dead scaffolding; it is re-introduced by the first story with a real permission surface (same decision as US-01/US-05). |
| A **forward-looking `IFolderFileProbe` seam** ships with a **no-op implementation** (`documents.folder_id` does not exist until US-04) | AC-5 blocks deleting a folder that contains **files**; wiring the check now as a seam lets US-09 fully specify and test the emptiness contract (subfolder arm live) and lets US-04 drop in the real probe without touching the delete handler. | Hard-coding "no files" inside the delete handler would bury the US-04 integration point and make the emptiness rule untestable as a unit; a real files query is impossible with no `folder_id` column yet. |
| **Two partial unique indexes** instead of one composite unique constraint | Postgres treats `NULL` parent_ids as distinct, so a single `UNIQUE(user_session_id, parent_id, lower(name))` would **not** enforce uniqueness among **root** folders (all have `parent_id IS NULL`). | A single index leaves root-level duplicates unguarded (AC-3 fails at the root); `NULLS NOT DISTINCT` (PG15+) is an alternative but the two explicit partial indexes are clearer and match the story's "partial unique index for root" decision. |

## Phase notes

- **Phase 0 (research.md)** — decisions: materialized-path format (self-inclusive `/A/B/C/`, leading+trailing
  slash), depth-by-segment-count rule, case-insensitive uniqueness via two partial `LOWER(name)` indexes +
  `23505`→code mapping, rename-O(1) (no descendant re-path), delete emptiness (direct-children now + file
  seam), `path` `text_pattern_ops` prefix index, sibling ordering, `FolderOptions` binding, the in-scope
  minimal folder-list read (D7).
- **Phase 1 (data-model.md, contracts/, quickstart.md)** — `Folder` aggregate + `FolderPath` VO + `FolderNode`
  read model; `FolderOptions`; the create/rename/delete/list contracts + the repository & file-probe seam
  contracts + the folders-table DDL; the runnable quickstart proving AC-1..AC-6.
- **Phase 2 (tasks.md)** — produced by `/speckit-tasks`, ordered Red→Green→Refactor per tier, with the
  concurrent-duplicate race (AC-3) and delete-under-concurrency (AC-5) integration tests last.
