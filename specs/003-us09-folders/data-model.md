# Phase 1 Data Model — Folder CRUD (US-09)

## Aggregate: `Folder` (`Modules/Folders/Domain/Folder.cs`)

Session-owned, audited node in the document tree. Implements `ISessionOwned` (stamped centrally) and
`IAuditable` (stamped by `AuditingInterceptor`). Construction goes through `Result`-returning factories
— **never throw for domain failures** (constitution §II).

| Property | Type | Notes |
|---|---|---|
| `Id` | `Guid` | Identity (GUID v4); last segment of `Path`. |
| `Name` | `string` | Trimmed, 1..`MaxNameLength`, no `/`. Display value. |
| `ParentId` | `Guid?` | `null` for root folders. Self-reference to another `Folder`. |
| `Path` | `string` | Materialized path `/{id}/…/{ownId}/` (D1). Immutable after creation. |
| `UserSessionId` | `Guid` | Owning session; stamped on insert, never in handlers. |
| `CreatedAt/By`, `ModifiedAt/By` | audit | Stamped by interceptor via `TimeProvider`. |

### Factories & behavior (all pure, `Result`-returning)

- `static Result<Folder> CreateRoot(string name, FolderNameRules rules)` — normalizes/validates the
  name (D4); builds `Path = "/" + id + "/"`; `ParentId = null`, depth 1.
- `static Result<Folder> CreateChild(Folder parent, string name, FolderNameRules rules, int maxDepth)`
  — validates name; rejects with `folder.max_depth_exceeded` when `parent.Path` already has `maxDepth`
  segments; builds `Path = parent.Path + id + "/"`, `ParentId = parent.Id`.
- `Result Rename(string newName, FolderNameRules rules)` — normalizes/validates; sets `Name` only;
  **does not touch** `Path`/`ParentId`/descendants (D5). Rename to the current (post-trim, case-folded)
  name is a success no-op.

> Uniqueness (D3) is **not** enforced in the aggregate — it is a database constraint; handlers surface
> `folder.duplicate_name` either from a best-effort pre-check or from the `23505` translation.

## Value object: `FolderPath` (`Modules/Folders/Domain/FolderPath.cs`)

Pure helper wrapping the path string. `Segments` (ordered ids), `Depth` (segment count), `Append(Guid)`
→ new `FolderPath`, `IsPrefixOf(FolderPath)` / `Contains(Guid)` for subtree reasoning. Keeps path
formatting in one tested place.

## Read model: `FolderNode` (`Modules/Folders/Features/ListFolders/FolderNode.cs`)

Flat projection returned by `ListFoldersQuery`, ordered by `LOWER(name)` (D8). The client builds the
tree from `ParentId`.

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` | |
| `ParentId` | `Guid?` | `null` at root. |
| `Name` | `string` | |
| `Depth` | `int` | From `Path` segment count; lets the UI hide "New folder" at max depth. |

## Options: `FolderOptions` (`Modules/Folders/FolderOptions.cs`)

Bound from `Folders:*` (D9). `MaxDepth` (default 3), `MaxNameLength` (default 100). A small
`FolderNameRules` record (`MaxNameLength`, reserved separator `/`) is derived from options and passed
to the domain factories so validation stays config-driven without the domain depending on `IOptions`.

## Seams

- `IFolderRepository` (`Domain/`): `AddAsync(Folder)`, `GetByIdAsync(Guid)` (session-filtered → `null`
  cross-session), `GetParentAsync(Guid)` (for depth/path on create), `HasChildrenAsync(Guid)`,
  `ListForSessionAsync()` (ordered by `LOWER(name)`), `Remove(Folder)`. Implemented by
  `Infrastructure/…/FolderRepository`.
- `IFolderFileProbe` (`Domain/`): `Task<bool> HasFilesAsync(Guid folderId, CancellationToken)` — the
  file arm of the emptiness rule. US-09 implementation `NoFolderFilesProbe` returns `false`; **US-04
  replaces it** with a `documents.folder_id` query (Complexity Tracking).

## Persistence — `folders` table (`AddFolders` migration)

Columns: `id` (PK), `user_session_id`, `parent_id` (NULL, FK → `folders.id`, **no cascade**), `name`,
`path` (text), audit columns (`created_at/by`, `modified_at/by`), mirroring `DocumentConfiguration`.

Indexes:

| Index | Definition | Serves |
|---|---|---|
| `ix_folders_user_session_id` | `(user_session_id)` | §III isolation, all session reads. |
| `ux_folders_root_name` | `UNIQUE (user_session_id, LOWER(name)) WHERE parent_id IS NULL` | AC-3 at root; case-insensitive. |
| `ux_folders_child_name` | `UNIQUE (user_session_id, parent_id, LOWER(name)) WHERE parent_id IS NOT NULL` | AC-3 nested; case-insensitive. |
| `ix_folders_path` | `(path text_pattern_ops)` | subtree `LIKE prefix || '%'` (delete check now, US-13 later). |

The `LOWER(name)` uniqueness and partial predicates are expressed via raw SQL in the migration
(`migrationBuilder.Sql`) since EF's fluent `HasIndex` cannot model a functional + partial unique index
directly; `FolderConfiguration` maps columns, the self-FK, and `ix_folders_user_session_id`.

## Validation rules → requirement trace

| Rule | Enforced where | Requirement |
|---|---|---|
| Depth ≤ 3 | `Folder.CreateChild` (domain) + UI hide at max depth | AC-2 / FR-003 / FR-012 |
| Name unique per parent, case-insensitive | 2 partial unique indexes + `23505`→code | AC-3 / FR-004 / FR-005 |
| Name trimmed, 1..100, no `/` | `Folder` factories / `Rename` (domain) | AC-6 / FR-007 |
| Rename leaves path & descendants | `Rename` sets name only | AC-4 / FR-006 |
| Delete only when empty (subfolders + files) | delete handler: `HasChildrenAsync` + `IFolderFileProbe`, one txn | AC-5 / FR-008 / FR-009 |
| Cross-session → not found | `GetByIdAsync` via global query filter | FR-010 |
| Sibling ordering | `ListForSessionAsync` `ORDER BY LOWER(name)` | FR-013 |
