# Phase 1 Data Model — US-11

No new entities, columns, or migration. A move mutates existing `folders` rows in one transaction.

## Mutated: Folder (existing) → table `folders`

| Field | Change |
|---|---|
| `parent_id` | Set on the **moved** folder only (the target folder id, or `null` for root). |
| `path` | Rewritten (prefix replace) on the **moved folder and every descendant** — one bulk `UPDATE`. |
| `name` | Unchanged (a duplicate name in the target is refused, not renamed). |
| everything else | Unchanged. Documents (`documents.folder_id`) are **not** touched — they follow their folder. |

- Cycle rule: `moved.Path.IsPrefixOf(target.Path)` (or `moved.Id == target.Id`) ⇒ refuse.
- Depth rule: `target.Path.Depth + (maxDescendantDepth − moved.Path.Depth) + 1 ≤ FolderOptions.MaxDepth`.
- New prefix: `target.Path.Value + movedId + '/'` (target folder) or `FolderPath.ForRoot(movedId).Value` (root).

## Error catalog delta (Folders module)

| Code | Type → status | Meaning |
|---|---|---|
| `folder.not_found` (existing) | NotFound → 404 | The folder or the target isn't in the current session. |
| `folder.max_depth_exceeded` (existing) | Validation → 400 | The result would nest deeper than the maximum. |
| `folder.duplicate_name` (existing) | Conflict → 409 | The target already has a folder with the same name. |
| `folder.conflict` (existing) | Conflict → 409 | A concurrent change conflicted; retry. |
| **`folder.circular_move`** (new) | Conflict → 409 | The target is the folder itself or one of its descendants. |

## Contract shapes

| Shape | Fields |
|---|---|
| `MoveFolderRequest` (API body) | `parentId: Guid?` (null = root) |
| `MoveFolderCommand` | `FolderId: Guid`, `TargetParentId: Guid?` |
| `IFolderMoveRepository` | `GetByIdAsync(id)`, `MaxSubtreeDepthAsync(pathPrefix)`, `SiblingExistsAsync(parentId, name)`, `MoveAsync(movedId, newParentId, oldPrefix, newPrefix)` |
| Frontend `TreeStore.moveFolder` | `(folderId: string, targetParentId: string \| null)` → optimistic re-parent + `PATCH` + rollback/refresh |

## Frontend state (existing `TreeStore`, extended)

- Reuses the raw `folders`/`documents` signals + `roots` computed (`buildForest` nests by `parentId`).
- `moveFolder` optimistically rewrites the moved folder's `parentId` (subtree re-nests), then `PATCH`; reverts on
  failure (with `moveError`), refreshes on success to correct `path`/`depth`.
- `isDescendant(targetId, movedId)` walks the `parentId` chain — powers both the drag enter-predicate and the
  menu's valid-target filter.
