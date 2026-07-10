# Contract — Folders API (US-09)

All endpoints are session-scoped by the persistence layer (US-01 cookie → `ISessionContext` → global
query filter). Failures return an RFC 9457 **ProblemDetails** with a stable `code` from `FolderErrors`
(constitution §II) — never a naked 500. A folder id owned by another session behaves as **404**
(`folder.not_found`), never 403 (§III).

## Error catalog (`FolderErrors`)

| Code | ErrorType → HTTP | Meaning |
|---|---|---|
| `folder.invalid_name` | Validation → 400 | Empty after trim, > `MaxNameLength`, or contains `/`. |
| `folder.max_depth_exceeded` | Validation → 400 | Parent already at `MaxDepth`. |
| `folder.duplicate_name` | Conflict → 409 | Sibling with same name (case-insensitive) in the parent. |
| `folder.not_empty` | Conflict → 409 | Folder still holds subfolders and/or files. |
| `folder.not_found` | NotFound → 404 | No such folder in this session (incl. cross-session). |
| `folder.conflict` | Conflict → 409 | Infra-level persistence conflict fallback. |

## POST `/api/folders` — create

Dispatches `CreateFolderCommand(Name, ParentId)` (`ICommand<Guid>`).

Request:

```json
{ "name": "Umowy", "parentId": null }
```

- `parentId: null` → root folder; a GUID → child of that folder.
- **201 Created** → `{ "id": "<guid>" }` (body is the new folder id).
- Failures: `folder.invalid_name` (400), `folder.max_depth_exceeded` (400 — parent at max depth),
  `folder.duplicate_name` (409), `folder.not_found` (404 — `parentId` not in session).

## PUT `/api/folders/{id}/name` — rename

Dispatches `RenameFolderCommand(Id, NewName)` (`ICommand`).

Request:

```json
{ "name": "Umowy 2026" }
```

- **204 No Content** on success (name only changes; path/descendants untouched — AC-4). Renaming to the
  current name (post-trim, case-folded) is also 204 (no-op).
- Failures: `folder.invalid_name` (400), `folder.duplicate_name` (409 — sibling clash),
  `folder.not_found` (404).

## DELETE `/api/folders/{id}` — delete

Dispatches `DeleteFolderCommand(Id)` (`ICommand`).

- **204 No Content** when the folder is empty (no subfolders, no files) — AC-5.
- Failures: `folder.not_empty` (409 — has subfolders and/or files), `folder.not_found` (404).
- Emptiness check + delete run in one transaction (FR-009).

## GET `/api/folders` — list (minimal, US-09 scope; extended by US-07)

Dispatches `ListFoldersQuery()` → `IReadOnlyList<FolderNode>`, ordered by `LOWER(name)` (FR-013).

- **200 OK**:

```json
[
  { "id": "A", "parentId": null, "name": "Umowy",   "depth": 1 },
  { "id": "B", "parentId": "A",  "name": "2026",    "depth": 2 }
]
```

- Only the current session's folders; the client composes the tree from `parentId`. Documents are **not**
  part of this response (US-07).

## Internal seams (not HTTP)

- `IFolderRepository` — `AddAsync`, `GetByIdAsync` (session-filtered), `GetParentAsync`,
  `HasChildrenAsync`, `ListForSessionAsync` (ordered), `Remove`.
- `IFolderFileProbe.HasFilesAsync(folderId)` — file arm of AC-5 emptiness. US-09: `NoFolderFilesProbe`
  returns `false`; **US-04** replaces it with a real `documents.folder_id` query. This is the single
  integration point US-04 touches to complete AC-5 end-to-end.
