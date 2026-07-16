# Phase 0 Research — US-12 Operacje zbiorcze (bulk move / delete)

## D1 — Carrying the per-id failure list (clarify Q1)

**Decision**: The handlers return a small `BulkResult` — one of `Success()`, `BadRequest(Error)` (empty / over-cap
→ a `400` via the normal `ProblemResults`), or `ValidationFailed(IReadOnlyList<BulkFailure>)` (per-id →
`422`). `BulkFailure(Guid Id, string Code)`. The endpoint maps it to `204` / `400` / a `422` ProblemDetails built
by a `BulkProblemResults.ValidationFailed(failures)` helper (`code: document.bulk_validation_failed` + a
`failures: [{ id, code }]` extension + `traceId`).

**Rationale**: The single-`Error` `Result<T>` can't carry a list; `BulkResult` is the minimal shape that still
resolves to **one** code-based ProblemDetails wire outcome (constitution §II). `422 Unprocessable Entity` is the
correct status for a well-formed request whose items fail business validation; `ErrorStatusMapper` has no `422`, so
the bulk failure is built directly (not via `ProblemResults.Problem(error)`), while empty/over-cap stay ordinary
`Validation` errors → `400`.

**Alternatives rejected**: a `200` result body (breaks failure→ProblemDetails-with-`code`); overloading `Error`'s
`Message` with a serialized list (unparseable, ugly).

## D2 — All-or-nothing: validate-all then apply, one transaction

**Decision**: Each handler (a) de-dupes the id list; (b) rejects empty / over-`BulkOptions.MaxItems` up front
(`400`); (c) loads the requested documents via `IDocumentBulkRepository.GetByIdsAsync` (session-filtered); (d)
builds the failure list — a requested id **not** returned ⇒ `document.not_found`, a returned `Origin == Demo` ⇒
`document.read_only`, and (move only) a missing target folder ⇒ one failure `{ id: targetFolderId, code:
folder.not_found }`; (e) if **any** failure ⇒ `ValidationFailed` (no write); else apply all in **one** transaction
(`MoveAllAsync` / `DeleteAllAsync`).

**Rationale**: Validating the whole set before any mutation is what makes it all-or-nothing; the transaction makes
a partial apply impossible even under a mid-op fault. A foreign/unknown id is simply absent from the session-
filtered read ⇒ reported as not-found (no existence disclosure, §III).

**Alternatives rejected**: per-item best-effort with partial success (explicitly rejected by the story — a
half-applied bulk delete is worse than none); loading per-id in a loop (N queries — one `WHERE id = ANY(@ids)` read
is enough).

## D3 — Transactional apply (`IDocumentBulkRepository`)

**Decision**: `IDocumentBulkRepository`:
- `GetByIdsAsync(IReadOnlyCollection<Guid> ids)` → the session's documents among `ids` (tracked; `WHERE id = ANY`).
- `MoveAllAsync(documents, targetFolderId)` → set each `FolderId`, one `SaveChanges` (no vector-index change).
- `DeleteAllAsync(documents)` → one transaction: remove all rows (the `chunks` FK cascades), commit, then
  best-effort blob cleanup per document (log-and-swallow, as US-08).

**Rationale**: Reuses the US-08 cascade + best-effort-blob pattern, extended to a set. One `SaveChanges` / one
transaction gives atomicity. The quota drops naturally as rows are removed (US-05 counts documents).

## D4 — Bulk list cap

**Decision**: `BulkOptions.MaxItems = 50` (SectionName `Bulk`), registered in `Program.cs`; over-cap ⇒
`document.bulk_too_large` (`400`). Empty ⇒ `document.bulk_empty` (`400`).

**Rationale**: Config-driven (§V/§VII, quota-ready); a small cap bounds the transaction. Distinct `400` codes let
the frontend message precisely.

## D5 — Frontend selection + bulk actions

**Decision**: A `SelectionStore` holds `selected: Set<string>` (ticked document ids) + `failedIds: Set<string>`
(from a `422`), with `toggle` / `clear` / `has` / `count` / `selectedIds`, and `bulkMove(targetFolderId)` /
`bulkDelete()` that `POST` the id list, then on success **clear the selection + refresh the tree and quota**, on
`422` set `failedIds` (highlight), on other errors a design-system notice. Document leaf rows get a checkbox; a
**bulk action bar** ("N zaznaczonych: Przenieś do… | Usuń | Anuluj") shows while a selection exists; move opens a
folder picker (reusing the US-10 pattern), delete opens a **design-system confirm** (count + names, never
`window.confirm`).

**Rationale**: The store owns the cross-cutting selection + the two API calls + the post-success refresh, keeping
the tree component thin; `failedIds` drives the AC-4/FR-009 marking. Shift-click range within a folder is a small
enhancement over the checkbox toggle (compute the contiguous slice of the folder's document rows).

## D6 — Range select (Shift-click)

**Decision**: Shift-click selects the contiguous range of document rows **within the same folder** between the
last-clicked and the shift-clicked row; a plain click toggles one. The store exposes `selectRange(folderDocIds,
fromId, toId)`.

**Rationale**: Matches the AC-1 "(lub Shift+klik dla zakresu w obrębie folderu)"; scoping to one folder keeps the
range unambiguous. A pure enhancement — plain checkbox selection remains the primary, always-available path.
