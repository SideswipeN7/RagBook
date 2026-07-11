# Contract — Tree API (US-07)

Session-scoped by the persistence layer (US-01). Read-only; no mutations in this slice.

## GET `/api/tree` — the session's folders + documents in one response

Dispatches `GetTreeQuery` → `TreeResponse`. Both lists are pre-ordered by the server (FR-008).

- **200 OK** → `TreeResponse`:

```json
{
  "folders": [
    { "id": "<guid>", "parentId": null, "name": "Umowy", "depth": 1 },
    { "id": "<guid>", "parentId": "<guid>", "name": "2026", "depth": 2 }
  ],
  "documents": [
    {
      "id": "<guid>",
      "folderId": "<guid|null>",
      "fileName": "umowa.pdf",
      "contentType": "application/pdf",
      "sizeBytes": 12345,
      "status": "Ready",
      "chunkCount": 8,
      "uploadedAt": "2026-07-11T10:00:00+00:00",
      "failureReason": null
    }
  ]
}
```

- `folders` ordered by `LOWER(name)`; `documents` ordered by `uploadedAt` descending.
- `folderId: null` → a root document (top level in the tree).
- `status` ∈ `Processing | Ready | Failed`. `failureReason` is non-null only for some `Failed` documents
  (populated by US-06); the client shows a generic message when a failed document has none.
- Demo-origin documents are **excluded** (the demo section is separate — FR-013).
- A fresh session returns empty `folders` and `documents` (the client shows the empty state, FR-007).
- Cross-session folders/documents never appear (FR-012).

## Behavior notes

- **One request, no N+1** (FR-001/SC-001): the server runs exactly two queries (folders, documents)
  regardless of folder count; the client composes the nested tree from `parentId`/`folderId`.
- The response is a projection — no new domain state; `failureReason` reads the new nullable
  `documents.failure_reason` column.

## Internal seam (not HTTP)

- `ITreeReader.GetAsync(ct)` → `TreeData(folders, documents)` — the single Tree-owned seam; the
  Infrastructure implementation runs the two session-scoped, ordered `AsNoTracking` queries. Keeps the
  Tree core free of Folders/Documents module references (constitution §I).
