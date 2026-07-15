# Contract — Move document (US-10)

One new route on the existing `/api/documents` resource; everything else (upload, delete, tree) is unchanged.

## `PATCH /api/documents/{id}/folder`

Move a document to a target folder, or to the root.

```
body: { "folderId": "<guid>" | null }   // null = root (no folder)
```

- `204 No Content` — moved (or a no-op when already in the target folder).
- `404` ProblemDetails `document.not_found` — the document isn't in the current session (incl. cross-session; no
  disclosure).
- `404` ProblemDetails `folder.not_found` — the target folder (when not null) isn't in the current session.
- `409` ProblemDetails `document.read_only` — the document is a read-only demo document and can't be moved.

### Handler behaviour (`MoveDocumentCommandHandler`)

1. Load the document (session-filtered). `null` → `document.not_found`.
2. `Origin == Demo` → `document.read_only`.
3. `TargetFolderId is Guid f` and not `IFolderReference.ExistsInSessionAsync(f)` → `folder.not_found`.
4. `document.FolderId == TargetFolderId` → **no-op**, `204` (no write).
5. otherwise `document.MoveToFolder(TargetFolderId)` + save → `204`.

### Invariants

- Only `documents.folder_id` changes; the document's chunks/vectors are untouched (SC-003).
- Session isolation: another session's document/folder reads as absent → 404 (never 403/disclosure).
- The endpoint is idempotent for an unchanged folder (no-op → 204).

## Frontend consumption

- Drag-and-drop **and** the "Przenieś do…" menu both call `TreeStore.moveDocument(documentId, targetFolderId)`,
  which optimistically updates the tree, issues the `PATCH`, and **rolls back** + shows a notice on any non-2xx.
- A drop onto the current folder issues **no** request (client-side no-op — FR-006 / SC-005).
