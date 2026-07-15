# Phase 1 Data Model — US-10

No new entities, columns, or migration. The move mutates one existing field.

## Mutated: Document (existing) → table `documents`

| Field | Change |
|---|---|
| `folder_id` (`uuid null`) | The **only** field written by a move — set to the target folder id, or `null` for the root. |
| everything else | Unchanged — including the document's chunks/vectors (a move does **not** re-index). |

- New domain behaviour: `Document.MoveToFolder(Guid? folderId)` sets `FolderId` (private setter). No-op detection
  (already in that folder) is handled by the caller (the handler) before invoking it.
- `Origin` (`User` | `Demo`) is read for the read-only guard; a `Demo` document is refused.

## Relationships (unchanged)

- `documents.folder_id → folders.id` (nullable; `null` = root). The target folder must exist in the current
  session (checked via `IFolderReference`) — no FK cascade change.
- `documents 1—* chunks` — untouched by a move (SC-003).

## Error catalog delta (Documents module)

| Code | Type → status | Meaning |
|---|---|---|
| `document.not_found` (existing) | NotFound → 404 | The document isn't in the current session. |
| `folder.not_found` (existing `TargetFolderNotFound`) | NotFound → 404 | The target folder isn't in the current session. |
| **`document.read_only`** (new) | Conflict → 409 | The document is a read-only demo document and can't be moved. |

## Contract shapes

| Shape | Fields |
|---|---|
| `MoveDocumentRequest` (API body) | `folderId: Guid?` (null = root) |
| `MoveDocumentCommand` | `DocumentId: Guid`, `TargetFolderId: Guid?` |
| Frontend `TreeStore.moveDocument` | `(documentId: string, targetFolderId: string \| null)` → optimistic + `PATCH` + rollback |

## Frontend state (existing `TreeStore`, extended)

- Raw `documents` (and `folders`) held in signals; `roots` recomposed from them.
- A move optimistically rewrites the target document's `folderId` in the `documents` signal (tree recomposes),
  then reverts it on a failed `PATCH`. A `moveError` signal carries the rollback reason for the notice.
