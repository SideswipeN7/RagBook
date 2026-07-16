# Phase 1 Data Model — US-12

No new entities, columns, or migration. Bulk operations mutate existing `documents` (and cascade `chunks`).

## Mutated: Document / Chunk (existing)

| Operation | Effect |
|---|---|
| bulk **move** | each selected document's `folder_id` → the target (or `null` for root); chunks/vectors untouched; one `SaveChanges`. |
| bulk **delete** | each selected document row removed; its `chunks` removed by FK cascade; best-effort blob cleanup; quota (document count) drops by N; one transaction. |

## New value types (Documents module)

| Type | Shape |
|---|---|
| `BulkFailure` | `record (Guid Id, string Code)` — one offending item + its reason code. |
| `BulkResult` | `Success()` \| `BadRequest(Error)` (empty/over-cap → 400) \| `ValidationFailed(IReadOnlyList<BulkFailure>)` (per-id → 422). |

## Error catalog delta (Documents module)

| Code | Where | Meaning |
|---|---|---|
| **`document.bulk_validation_failed`** (new, const) | 422 top-level `code` | One or more selected items failed validation (see `failures[]`). |
| **`document.bulk_empty`** (new, Validation → 400) | 400 | The id list was empty. |
| **`document.bulk_too_large`** (new, Validation → 400) | 400 | The id list exceeded `BulkOptions.MaxItems`. |
| `document.not_found` (existing) | a `failures[]` code | An id not in the session (foreign / unknown / already gone). |
| `document.read_only` (existing) | a `failures[]` code | A read-only demo document in the selection. |
| `folder.not_found` (existing) | a `failures[]` code | The bulk-move target folder isn't in the session (`{ id: targetFolderId }`). |

## Contract shapes

| Shape | Fields |
|---|---|
| `BulkMoveRequest` (API body) | `ids: Guid[]`, `targetFolderId: Guid?` (null = root) |
| `BulkDeleteRequest` (API body) | `ids: Guid[]` |
| `BulkMoveCommand` / `BulkDeleteCommand` | `Ids: IReadOnlyList<Guid>` (+ `TargetFolderId` for move) → `BulkResult` |
| `422` body (ProblemDetails) | `{ code: "document.bulk_validation_failed", failures: [{ id, code }], traceId, detail, status: 422 }` |
| `IDocumentBulkRepository` | `GetByIdsAsync(ids)`, `MoveAllAsync(docs, targetFolderId)`, `DeleteAllAsync(docs)` |

## Config

| Option | Default | Section |
|---|---|---|
| `BulkOptions.MaxItems` | 50 | `Bulk` |

## Frontend state (`SelectionStore`)

- `selected: Set<string>` (ticked document ids); `failedIds: Set<string>` (from a 422).
- `toggle(id)`, `selectRange(folderDocIds, fromId, toId)`, `clear()`, `has(id)`, `count`, `selectedIds`.
- `bulkMove(targetFolderId)` / `bulkDelete()` → POST; success ⇒ clear + refresh tree/quota; 422 ⇒ set `failedIds`.
