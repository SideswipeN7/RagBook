# Contract — Move folder (US-11)

One new route on the existing `/api/folders` resource; folder CRUD (US-09) and document endpoints are unchanged.

## `PATCH /api/folders/{id}/parent`

Move a folder (with its whole subtree) under a target folder, or to the root.

```
body: { "parentId": "<guid>" | null }   // null = root (top-level)
```

- `204 No Content` — moved (or a no-op when the parent is unchanged).
- `404` ProblemDetails `folder.not_found` — the folder or the target isn't in the current session (incl.
  cross-session; no disclosure).
- `409` ProblemDetails `folder.circular_move` — the target is the folder itself or one of its descendants.
- `400` ProblemDetails `folder.max_depth_exceeded` — the resulting nesting would exceed the maximum depth.
- `409` ProblemDetails `folder.duplicate_name` — the target already contains a folder with the same name.

### Handler behaviour (`MoveFolderCommandHandler`)

1. Load the folder (session-filtered). `null` → `folder.not_found`.
2. `TargetParentId == folder.ParentId` → **no-op**, `204` (no write).
3. `TargetParentId is Guid p`: load the target (session-filtered). `null` → `folder.not_found`.
   - `moved.Path.IsPrefixOf(target.Path)` (or `moved.Id == p`) → `folder.circular_move`.
   - `target.Depth + (maxDescendantDepth − moved.Depth) + 1 > MaxDepth` → `folder.max_depth_exceeded`.
   - a same-named sibling exists under the target → `folder.duplicate_name`.
4. One transaction: bulk `UPDATE` of `path` for the folder + descendants (**session-scoped**) + `UPDATE parent_id`
   of the moved folder → `204`.

### Invariants

- Atomic: no observable partial move (one transaction).
- Only `folders` change — `documents.folder_id` (and the vector index) are untouched.
- Session isolation: another session's folder/target → 404; the bulk `UPDATE` is constrained to the session.
- Idempotent for an unchanged parent (no-op → 204).

## Frontend consumption

- Drag-and-drop **and** the folder "Przenieś do…" menu both call `TreeStore.moveFolder(folderId, targetParentId)`,
  which optimistically re-parents (the subtree re-nests by `parentId`), `PATCH`es, and **rolls back** + shows a
  notice on any non-2xx (refresh on success corrects paths/depths).
- A folder is never offered as a drop target for itself or a descendant (`cdkDropListEnterPredicate` + the menu's
  filtered target list, via `isDescendant`).
- A drop onto the current parent issues **no** request (client-side no-op).
