# Quickstart — Validate US-09

## Prerequisites

- .NET 10 SDK, Node.js (Angular), Docker running (for integration tests / Aspire PostgreSQL).
- EF tooling restored once: `dotnet tool restore` (see AGENTS.md) before creating/applying migrations.

## Run locally (Aspire)

```sh
cd src/Web && npm install && cd -        # install SPA deps once
dotnet run --project src/RagBook.AppHost
# Aspire dashboard prints its URL; it starts PostgreSQL, the API, and the Angular dev server.
# The folder tree renders context actions: New folder / Rename / Delete. "New folder" is hidden at depth 3.
```

## Automated validation (the source of truth for DoD)

```sh
# Cheapest tiers first (no Docker)
dotnet test tests/RagBook.Domain.Tests            # path build, depth guard (AC-1/AC-2), name normalization + rename (AC-4/AC-6)
dotnet test tests/RagBook.Application.Tests        # Create/Rename/Delete/ListFolders handlers (mocked repo + fake probe)

# Integration tier — START DOCKER FIRST (Testcontainers PostgreSQL)
dotnet test tests/RagBook.Api.IntegrationTests     # AC-1/AC-3 race/AC-5/ordering/cross-session over a real DB

# Frontend
cd src/Web && npm test                             # folder-tree actions; "New folder" hidden at max depth
```

Tests map to acceptance criteria:

| AC | Tier | Test (`Should_..._When_...`) | Proves |
|---|---|---|---|
| AC-1 | Domain | `Should_BuildChildPathUnderParent_When_CreatingChild` | child `Path = parent.Path + id + "/"`, depth+1 |
| AC-2 | Domain | `Should_ReturnMaxDepthExceeded_When_ParentAtMaxDepth` | child of depth-3 → `folder.max_depth_exceeded`, nothing built |
| AC-6 | Domain | `Should_ReturnInvalidName_When_NameEmptyAfterTrimOrTooLongOrHasSlash` | trim → empty / >100 / `/` → `folder.invalid_name` |
| AC-4 | Domain | `Should_ChangeNameOnly_When_Renaming` | `Rename` sets name; `Path`/`ParentId` unchanged |
| no-op | Domain | `Should_Succeed_When_RenamingToCurrentName` | rename to same (trimmed/case-folded) name → success no-op |
| AC-3 | Application | `Should_ReturnDuplicateName_When_SiblingNameExistsCaseInsensitive` | pre-check maps "umowy" vs "Umowy" → `folder.duplicate_name` |
| AC-5 | Application | `Should_ReturnNotEmpty_When_FolderHasChildOrFiles` | `HasChildrenAsync`/probe true → `folder.not_empty`, nothing removed |
| AC-1 | Integration | `Should_PersistHierarchy_When_CreatingRootThenChild` | real rows; `GET /api/folders` shows child under parent, ordered |
| AC-3 | Integration | `Should_RejectDuplicate_When_SameNameInSameParent` | 2nd "Umowy" at root → 409; "Umowy" under other parent → 201 |
| **AC-3 race** | Integration | `Should_AdmitAtMostOne_When_TwoIdenticalCreatesRaceInSameParent` | concurrent duplicate creates → exactly one row, other `folder.duplicate_name` |
| AC-4 | Integration | `Should_LeaveDescendantsInPlace_When_RenamingNonEmptyFolder` | rename parent → children keep `path`/position |
| AC-5 | Integration | `Should_DeleteEmpty_And_BlockNonEmpty` | empty → 204; with subfolder → 409 `folder.not_empty` |
| FR-013 | Integration | `Should_OrderSiblingsCaseInsensitiveAlphabetically` | list order independent of creation order |
| FR-010 | Integration | `Should_Return404_When_OperatingOnAnotherSessionsFolder` | cross-session id → `folder.not_found` (404) |

## Files arm of delete (AC-5) — forward-looking

- `documents.folder_id` does not exist until US-04, so `IFolderFileProbe` ships as `NoFolderFilesProbe`
  (returns `false`). AC-5's "blocked when it contains a **file**" arm is unit-tested here with a **fake
  probe returning true**; the real files query is dropped in by US-04 replacing the probe — no change to
  the delete handler.

## Manual smoke (optional)

```sh
curl -i -c jar -b jar -H "Content-Type: application/json" \
  -d '{"name":"Umowy","parentId":null}' http://localhost:<api>/api/folders      # → 201 {"id":"..."}
curl -i -c jar -b jar http://localhost:<api>/api/folders                         # → 200 [ {Umowy, depth 1}, ... ]
# create a second "umowy" at root → 409 folder.duplicate_name (case-insensitive)
# delete a folder that has a child → 409 folder.not_empty
```

## Expected outcomes

- Creating a root folder then a child yields the correct parent/child hierarchy in `GET /api/folders`.
- Nesting past depth 3, duplicate names in a parent (case-insensitively), invalid names, and deleting a
  non-empty folder each fail with the correct stable code before/without persisting a change.
- A rename changes only the name; descendants keep their place and path.
- Two concurrent identical creates in one parent yield exactly one folder; the other gets
  `folder.duplicate_name`.
- Another session's folder id is indistinguishable from a non-existent one (404).
