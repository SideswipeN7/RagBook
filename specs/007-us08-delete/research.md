# Phase 0 Research — Delete Document (US-08)

Small story on a mature base; decisions are fixed by the spec/constraints and existing US-04/05/06/07 code.

## D1 — DB-first, then best-effort blob (orphan tolerated)

- **Decision**: The delete runs in this order: **(1)** delete the document row inside a transaction — the
  `chunks.document_id` FK `ON DELETE CASCADE` (US-06) removes the chunks — and **commit**; **(2)** attempt
  `IFileStorage.DeleteAsync(storagePath)` as **best-effort**, catching and **logging** any failure without
  failing the operation. The whole thing is encapsulated in `DocumentDeletionRepository` (Infrastructure).
- **Rationale**: The database is the source of truth; committing first guarantees the record + index are
  gone even if the object store hiccups. A rare orphaned blob is a logged, accepted MVP trade-off (no
  cross-store transaction exists). Chunk cleanup lives in the DB (one source of consistency), not app code.
- **Alternatives**: blob-first (could delete the file then fail the DB delete → dangling record pointing at
  a missing blob); app-level chunk deletion (duplicates the cascade, risks drift).

## D2 — Session-scoped delete → 404, idempotent-from-user

- **Decision**: The repository loads/removes the document **through the session query filter**, so a
  document owned by another session (or already deleted / unknown) is invisible → the repository returns
  "not found" and the handler yields `document.not_found` (→ 404). A second delete of the same id is
  therefore also 404, which the UI treats as already-done.
- **Rationale**: Reuses the US-01 isolation guarantee (never 403, never leaks existence); idempotence falls
  out of the same 404 for a missing row.
- **Alternatives**: `IgnoreQueryFilters` + explicit owner check (re-implements what the filter already does).

## D3 — Delete during processing (reuse the US-06 quiet abort)

- **Decision**: Deleting a `Processing` document just deletes it (same path). The US-06
  `ProcessDocumentHandler` already reads the target first (`GetTargetAsync`) and **stops quietly** when the
  record is gone, and writes chunks only at the end via `ReplaceForDocumentAsync` — so a delete mid-run
  leaves no chunks and raises no error. US-08 adds an integration test asserting this, but no new code.
- **Rationale**: The worker was designed for this (US-06 AC-4/FR-013); US-08 relies on it rather than
  coordinating with in-flight processing.
- **Alternatives**: cancelling/locking the in-flight job (needless coordination for at-most-a-few chunks
  that the cascade would remove anyway if written before the delete commits).

## D4 — Thin handler, repo owns storage + logging

- **Decision**: `IDocumentDeletionRepository.DeleteAsync(Guid id, ct) → bool` (true = deleted, false = not
  found). The Infrastructure implementation does the transactional DB delete, the best-effort blob delete,
  and the logging (it has `IFileStorage` + `ILogger`). `DeleteDocumentCommandHandler` is trivial:
  `deleted ? Result.Success() : Result.Failure(DocumentErrors.NotFound)`.
- **Rationale**: Keeps Core free of storage/logging orchestration; one place owns the DB→blob ordering.
- **Alternatives**: handler orchestrates storage + logging (pushes infra concerns + `ILogger` into Core).

## D5 — Frontend: inline confirm on document leaves, then refresh

- **Decision**: The US-07 `app-document-tree` already renders document leaves and carries an inline-confirm
  pattern (used for folders). US-08 adds a **Delete** action on document leaves with the same inline
  confirmation (no native dialog) and a small `DocumentActionsStore.delete(id)` that issues
  `DELETE /api/documents/{id}` and, on success, calls `TreeStore.refresh()` + `QuotaStore.refresh()` so the
  row disappears and the quota counter drops without a reload.
- **Rationale**: Consistent with folder delete; no shared modal library exists yet (that is future UI work).
- **Alternatives**: a shared confirm-dialog component (scope creep for one action); `window.confirm`
  (forbidden by the constitution).
