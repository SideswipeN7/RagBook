# Contract — Bulk operations (US-12)

Two new routes on the existing `/api/documents` resource. Both are **all-or-nothing** (validate all → apply in one
transaction). The id list is de-duplicated server-side.

## `POST /api/documents/bulk-move`

```
body: { "ids": ["<guid>", …], "targetFolderId": "<guid>" | null }   // null = root
```
- `204 No Content` — all moved (only `folder_id` changes; chunks/vectors untouched).
- `400` ProblemDetails `document.bulk_empty` — empty id list.
- `400` ProblemDetails `document.bulk_too_large` — more than `BulkOptions.MaxItems` ids.
- `422` ProblemDetails `document.bulk_validation_failed` + `failures: [{ id, code }]` — one or more items invalid;
  **nothing moved**. Per-id codes: `document.not_found`, `document.read_only`, `folder.not_found`
  (`{ id: targetFolderId }` when the target folder is absent).

## `POST /api/documents/bulk-delete`

```
body: { "ids": ["<guid>", …] }
```
- `204 No Content` — all deleted (records + chunks by cascade); the quota drops by the number deleted.
- `400` ProblemDetails `document.bulk_empty` / `document.bulk_too_large`.
- `422` ProblemDetails `document.bulk_validation_failed` + `failures: [{ id, code }]` — one or more items invalid;
  **nothing deleted**. Per-id codes: `document.not_found`, `document.read_only`.

## `422` body (RFC 9457 ProblemDetails)

```jsonc
{
  "status": 422,
  "detail": "One or more selected items could not be processed.",
  "code": "document.bulk_validation_failed",
  "failures": [ { "id": "<guid>", "code": "document.read_only" }, … ],
  "traceId": "…"
}
```

## Handler behaviour (both)

1. De-dupe `ids`. Empty → `document.bulk_empty` (400). Count > `MaxItems` → `document.bulk_too_large` (400).
2. `GetByIdsAsync(ids)` (session-filtered). For each requested id: absent from the result → `document.not_found`;
   `Origin == Demo` → `document.read_only`. (Move: target folder absent in session → `folder.not_found`.)
3. Any failure → `422` with `failures[]`, **no write**. Else apply all in one transaction (`MoveAllAsync` /
   `DeleteAllAsync`) → `204`.

## Invariants

- All-or-nothing: 0 documents change on any validation failure.
- Session isolation: a foreign/unknown id is reported as `document.not_found` (never disclosing existence); no
  write touches another session.
- Move changes only `folder_id`; delete cascades chunks + lowers quota by N.

## Frontend consumption

- The **selection store** posts the ticked ids; on `204` it clears the selection and refreshes the tree + quota; on
  `422` it reads `failures[]` and marks those ids (`failedIds`); on `400` it shows the code-mapped message.
