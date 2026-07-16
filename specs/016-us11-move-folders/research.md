# Phase 0 Research — US-11 Przenoszenie folderów (z poddrzewem)

## D1 — Cycle detection

**Decision**: `moved.Path.IsPrefixOf(target.Path)` (materialized-path, one string comparison — already on
`FolderPath`). True ⇔ target is the moved folder or one of its descendants ⇒ `folder.circular_move`. Root target
(`parentId = null`) can never be a cycle.

**Rationale**: The materialized path makes "is X an ancestor-or-self of Y" an O(1) prefix check — the headline
advantage of the model. No recursive walk.

## D2 — Depth validation

**Decision**: New depth of the deepest node = `targetDepth + subtreeHeight + 1`, where `targetDepth = target.Path.Depth`
(0 for root), `subtreeHeight = maxDescendantDepth − moved.Path.Depth` (0 if the folder has no subfolders). Refuse
if `> FolderOptions.MaxDepth` → `folder.max_depth_exceeded`. `maxDescendantDepth` comes from a single query over
`path LIKE @movedPrefix || '%'` (session-scoped) counting path segments.

**Rationale**: One aggregate query gives the subtree height; the shift is arithmetic. Reuses `FolderOptions.MaxDepth`
(config, no magic number).

## D3 — Name-conflict check

**Decision**: Pre-check for a same-named sibling in the target (case-insensitive) → `folder.duplicate_name`, before
the write. The DB partial-unique index on `LOWER(name)` per parent is the backstop (a concurrent insert →
`FoldersExceptionHandler` maps `23505` → `duplicate_name`).

**Rationale**: A deterministic pre-check gives the clean error without depending on catching the unique violation;
the index still guards concurrency (AC-4 / edge cases).

## D4 — The move: one transaction, session-scoped raw SQL

**Decision**: `IFolderMoveRepository.MoveAsync(movedId, newParentId, oldPrefix, newPrefix, session)` runs one
transaction:
```sql
UPDATE folders SET path = @newPrefix || substring(path, length(@oldPrefix) + 1)
WHERE path LIKE @oldPrefix || '%' AND user_session_id = @session;

UPDATE folders SET parent_id = @newParentId
WHERE id = @movedId AND user_session_id = @session;
```
`oldPrefix = moved.Path.Value`; `newPrefix = target.Path.Value + movedId + '/'` (or `/movedId/` for root — via
`FolderPath.ForRoot`/`Append`). Documents are **not** touched (`folder_id` unchanged).

**Rationale**: Re-parents the whole subtree with a single prefix rewrite (the "przenoszenie poddrzewa jednym
UPDATE" headline). One transaction ⇒ no observable partial move (FR-002). **The bulk UPDATE MUST include
`user_session_id = @session`** because the EF global query filter does not apply to raw SQL (constitution §III
caveat) — a documented, tested requirement.

**Concurrency**: the moved folder's row is read `FOR UPDATE` inside the transaction; a concurrent move of the same
folder finds a stale prefix and its `LIKE` matches nothing (or the row lock serialises) → it fails cleanly rather
than corrupting paths. A `folder.conflict` (existing) surfaces if a concurrency conflict is detected.

**Alternatives rejected**: EF per-entity load + save of the whole subtree (N updates, no atomic prefix rewrite);
recursive CTE re-computation (needless — a prefix replace is exact for materialized paths).

## D5 — `folder.circular_move` error type

**Decision**: `FolderErrors.CircularMove = Error.Conflict("folder.circular_move", …)` (→ 409) — the requested move
conflicts with the tree's acyclic invariant.

**Rationale**: 409 matches the constitution's conflict mapping and the sibling `folder.*` conflict errors
(`duplicate_name`, `not_empty`).

## D6 — Frontend optimistic re-parent

**Decision**: `TreeStore.moveFolder(folderId, targetParentId: string | null)`:
1. no-op if `targetParentId === moved.parentId`.
2. optimistically set the moved folder's `parentId` in the `folders` signal → `buildForest` **re-nests the whole
   subtree by `parentId`** instantly (descendants chain to their immediate parents, which chain to the moved node).
3. `PATCH /api/folders/{id}/parent`; on success `refresh()` (corrects `path`/`depth` server-side); on error revert
   the `parentId` + set `moveError`.

**Rationale**: `buildForest` nests folders by `parentId` (not path), so re-parenting one node moves its subtree
with it — the optimistic step is a single-field change. `path`/`depth` are only stale between the optimistic
re-nest and the success-refresh (a brief window); refresh reconciles them.

**Alternatives rejected**: Rewriting every descendant's path/depth on the client (duplicates backend logic,
error-prone); no refresh (leaves `depth` stale, which gates "new subfolder" affordances).

## D7 — Excluding the subtree during a drag (+ menu)

**Decision**: The frontend has no `path` on folder DTOs, so "is target a descendant of moved" is computed by
walking the target's `parentId` chain up to the moved id (`isDescendant(targetId, movedId)` over the flat
`folders` list). A `cdkDropListEnterPredicate` rejects a folder drag when the target **is** the moved folder or a
descendant (AC-2 — descendants never highlight). The "Przenieś do…" **folder menu** lists folders filtered by the
same rule (excluding self + subtree) plus "Root".

**Rationale**: The `parentId` chain is already in the store; a path column isn't needed on the client. The same
`isDescendant` powers both the drag predicate and the menu's valid-target list, so they can't diverge.

## D8 — `onDrop` routing (folder vs document)

**Decision**: `cdkDragData` carries the tree node (`kind: 'folder' | 'document'`). `onDrop(node, targetFolderId)`
branches on `node.kind` → `moveFolder(node.id, targetFolderId)` or `moveDocument(node.id, targetFolderId)`.

**Rationale**: One drop handler, one code path per node type; documents keep US-10 behaviour untouched.
