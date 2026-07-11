# Phase 1 Data Model — Delete Document (US-08)

US-08 introduces **no schema change** — it reuses the US-04 `documents`, the US-06 `chunks` (with the
`document_id` FK `ON DELETE CASCADE`), and the US-04 `IFileStorage`. It adds one seam, one error, one
command.

## Seam: `IDocumentDeletionRepository` (`Modules/Documents/Domain/`)

```
Task<bool> DeleteAsync(Guid documentId, CancellationToken ct);
```

- Session-scoped: loads the document through the global query filter; a cross-session / already-deleted /
  unknown id is invisible → returns `false` (not found).
- On a hit: deletes the row in a transaction (the `chunks` FK cascade removes its chunks) and commits, then
  makes a **best-effort** `IFileStorage.DeleteAsync(storagePath)` (a failure is logged, not thrown) and
  returns `true`. A document with no stored blob simply skips the blob delete.

Implemented by `DocumentDeletionRepository` (Infrastructure) with `RagBookDbContext` + `IFileStorage` +
`ILogger`.

## Error: `DocumentErrors.NotFound`

`Error.NotFound("document.not_found", "The document does not exist.")` — the only failure code (→ 404),
returned for a cross-session / already-deleted / unknown id.

## Command + handler (`Modules/Documents/Features/DeleteDocument/`)

- **`DeleteDocumentCommand(Guid Id) : ICommand`** — the write.
- **`DeleteDocumentCommandHandler`** — `bool deleted = await repo.DeleteAsync(Id, ct); return deleted ?
  Result.Success() : Result.Failure(DocumentErrors.NotFound);`

## Frontend view model

- **`DocumentActionsStore`** (`core/document-actions.store.ts`): `delete(id)` → `DELETE /api/documents/{id}`
  → on success `TreeStore.refresh()` + `QuotaStore.refresh()`.
- **`app-document-tree`**: document leaves gain a **Delete** action + inline confirm (reusing the folder
  confirm state), calling `DocumentActionsStore.delete(id)`.

## Rules → requirement trace

| Rule | Where | Requirement |
|---|---|---|
| Confirmation required (no native dialog) | tree inline confirm | FR-001 |
| Hard delete of record + index | `DeleteAsync` (row) + cascade | FR-002 |
| Chunks cascade at the DB | US-06 FK `ON DELETE CASCADE` | FR-003 |
| DB-first (tx) → best-effort blob, orphan tolerated | `DocumentDeletionRepository` | FR-004 |
| Tree drops row + quota drops, no reload | `DocumentActionsStore` refresh | FR-005 |
| Cross-session → 404, untouched | session query filter → not found | FR-006 |
| Idempotent-from-user (2nd → 404) | same not-found path | FR-007 |
| Delete during processing → worker quiet-abort | US-06 `GetTargetAsync` null | FR-008 |
| Errors via Result → ProblemDetails code | `DocumentErrors.NotFound` | FR-009 |
| Deleted doc resolves as clean 404 | delete of gone id → 404 | FR-010 |
