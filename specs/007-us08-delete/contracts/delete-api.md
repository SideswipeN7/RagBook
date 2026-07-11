# Contract — Delete Document API (US-08)

Session-scoped by the persistence layer (US-01). A document owned by another session is invisible → 404
(`document.not_found`), never 403.

## DELETE `/api/documents/{id}` — delete a document and its index

Dispatches `DeleteDocumentCommand(id)` → `Result`.

- **204 No Content** → the document record and **all its chunks** are gone (chunks via the DB cascade); a
  best-effort blob delete followed. The tree drops the row and the quota drops on the client's refresh.
- **404 Not Found** → `document.not_found` (ProblemDetails, stable `code`) when the id is unknown, already
  deleted, or owned by another session. A second delete of the same id is also 404 (idempotent-from-user).

```json
// 404 body
{ "type": "...", "status": 404, "detail": "The document does not exist.", "code": "document.not_found" }
```

## Behavior notes

- **Order**: database first (transaction: delete row → chunks cascade → commit), then best-effort
  `IFileStorage.DeleteAsync`. A storage failure is **logged** and does **not** change the 204 — the record
  and index are already gone (orphaned blob tolerated).
- **During processing**: deleting a `Processing` document returns 204; the US-06 worker aborts quietly when
  it finds the record gone (no chunks written, no error).
- **Cross-session / repeat**: 404, target untouched.

## Internal seam (not HTTP)

- `IDocumentDeletionRepository.DeleteAsync(id, ct) → bool` — session-scoped transactional delete (chunks
  cascade) + best-effort blob cleanup + logging; `false` = not found. Impl `DocumentDeletionRepository`.

## Frontend

- `DocumentActionsStore.delete(id)` → `DELETE /api/documents/{id}` → on success `TreeStore.refresh()` +
  `QuotaStore.refresh()`; a 404 is ignored (already-done).
- `app-document-tree` document leaves: **Delete** action + inline confirm (no native dialog).
