# Feature Specification: Operacje zbiorcze na plikach — bulk move / delete (US-12)

**Feature Branch**: `017-us12-bulk-ops`

**Created**: 2026-07-16

**Status**: Draft

**Input**: User description: US-12 — select several documents and, in one action, move them to a folder or delete
them, with **all-or-nothing** semantics: if any selected item fails validation, the whole operation is rejected
with a per-item reason list and nothing changes.

## Context

Two bulk actions — **move** and **delete** — let the user tidy the knowledge base without repeating single-file
operations. Both are **all-or-nothing**: the server validates **every** selected item first and only then applies
the change, in one transaction; if any item is invalid (not the user's, or a read-only demo document, or a missing
target folder for a move), the whole operation is refused with a list of `{ id, reason }` and **nothing is moved
or deleted**. Only these two operations exist. This reuses US-08 (delete + cascade + quota), US-10 (move
validations + read-only), US-07 (the tree), and the session isolation of US-01. The list-size cap is
configuration-driven — the API is written "quota-ready" for a future tier that raises it.

## Clarifications

### Session 2026-07-16

- Q: How should the all-or-nothing bulk failure convey its per-id `{id, reason}` list? → A: **422 ProblemDetails + `failures[]`** — on a validation failure the response is `422 Unprocessable Entity`, RFC 9457 ProblemDetails with `code: document.bulk_validation_failed` and a `failures: [{ id, code }]` extension (keeping the failure→ProblemDetails-with-`code` convention; the frontend branches on `code` and reads `failures`). Empty / over-cap id lists remain a plain `400`.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Select files and see the action bar (Priority: P1) 🎯 MVP

The user ticks several documents; an action bar appears — "N zaznaczonych: Przenieś do… | Usuń | Anuluj".

**Why this priority**: Selection + the action bar is the entry point for every bulk action; without it there's
nothing to move or delete in bulk.

**Independent Test**: Tick two documents → the action bar shows the count and the Move / Delete / Cancel actions;
"Anuluj" clears the selection.

**Acceptance Scenarios**:

1. **Given** a list of documents, **When** the user ticks their checkboxes (or Shift-clicks a range within a
   folder), **Then** an action bar shows "N zaznaczonych" with "Przenieś do… | Usuń | Anuluj".
2. **Given** a selection, **When** the user clicks "Anuluj", **Then** the selection is cleared and the action bar
   disappears.

---

### User Story 2 - Bulk move (Priority: P1)

The user moves all selected documents into one folder (or the root) with a single action.

**Why this priority**: Reorganising many files at once is half the value of the story.

**Independent Test**: Select 3 documents from different folders; choose "Przenieś do…" → a folder; one request moves
them all; the tree shows them in the target.

**Acceptance Scenarios**:

1. **Given** 3 selected documents from different folders, **When** the user chooses "Przenieś do…" → "Archiwum",
   **Then** a single bulk-move request moves all three into "Archiwum" and the tree updates without a reload.

---

### User Story 3 - Bulk delete (Priority: P1)

The user deletes all selected documents at once, behind a confirmation, and the quota drops accordingly.

**Why this priority**: Bulk cleanup is the other half of the value.

**Independent Test**: Select 3 documents; "Usuń" → confirm (the dialog shows the count + names) → one transaction
deletes the records + their chunks; the quota drops by 3.

**Acceptance Scenarios**:

1. **Given** 3 selected documents, **When** the user clicks "Usuń" and confirms (a design-system dialog showing the
   count and names), **Then** one transaction deletes the records and their chunks (cascade) and the quota drops
   by 3.

---

### User Story 4 - All-or-nothing on failure (Priority: P1)

If any selected item can't be operated on, the whole bulk action is refused with per-item reasons and nothing
changes.

**Why this priority**: All-or-nothing is the safety contract that makes bulk actions trustworthy — a half-applied
bulk delete would be worse than none.

**Independent Test**: Include a read-only (demo) document in a bulk delete → the operation is refused with a
`{ id, reason }` list; the UI flags the offending item; no document is deleted.

**Acceptance Scenarios**:

1. **Given** a selection containing a read-only (demo) document, **When** the user attempts a bulk delete, **Then**
   the whole operation is rejected with a per-item reason list, the UI marks the problem item(s), and **no**
   document is deleted.
2. **Given** a selection whose target folder does not exist (bulk move), **When** the operation runs, **Then** it
   is rejected with a per-item reason and nothing moves.

---

### User Story 5 - Per-id ownership validation (Priority: P1)

Every id in the request is validated against the session; an id that isn't the user's is reported as "not found"
and (all-or-nothing) fails the whole operation.

**Why this priority**: Bulk endpoints must not become a way to touch another session's data or leak existence.

**Independent Test**: Send a bulk request whose id list includes another session's document id → the operation is
refused; that id is reported as "not found"; nothing changes.

**Acceptance Scenarios**:

1. **Given** an id list containing another session's document, **When** the request arrives, **Then** the operation
   is refused (consistent with all-or-nothing) and that id is reported as "not found" (existence not disclosed).

### Edge Cases

- **Empty id list** → rejected (bad request); no operation.
- **Duplicate ids** in the list → de-duplicated server-side before validation.
- **A document deleted in another tab** before the operation → reported in the validation failure (all-or-nothing).
- **A list exceeding the configured size cap** → rejected with a clear reason (no operation).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST provide exactly two bulk operations over a list of document ids — **move** (to a
  folder or the root) and **delete** — each a single request.
- **FR-002**: Both operations MUST be **all-or-nothing**: the system validates **every** id before applying any
  change, in one transaction; if any id fails validation, **no** document is moved or deleted.
- **FR-003**: On a validation failure, the system MUST return **`422` ProblemDetails** with `code:
  document.bulk_validation_failed` and a **`failures: [{ id, code }]`** extension naming every offending item; the
  reason codes distinguish at least: not found (not the session's / unknown / already gone), read-only (demo), and
  — for move — missing target folder. (Empty / over-cap id lists remain a plain `400`.)
- **FR-004**: Each id MUST be validated against the current session; an id not owned by the session is reported as
  **not found** (never disclosing existence), and fails the operation.
- **FR-005**: A **read-only (demo)** document in the selection MUST cause the whole operation to be refused (it
  cannot be bulk-moved or bulk-deleted).
- **FR-006**: The id list MUST be **de-duplicated** server-side; an **empty** list MUST be rejected; a list beyond
  the **configured size cap** MUST be rejected with a clear reason.
- **FR-007**: A bulk **move** MUST change only the documents' owning folder (not their indexed content); a bulk
  **delete** MUST remove the records and their chunks (cascade) and MUST decrease the quota by the number deleted.
- **FR-008**: The frontend MUST let the user select multiple documents (checkboxes; optionally Shift-click for a
  range within a folder), show an action bar ("N zaznaczonych: Przenieś do… | Usuń | Anuluj"), confirm a bulk
  delete via a **design-system dialog** (showing the count + names, never a native dialog), clear the selection on
  success, and refresh the tree + quota without a reload.
- **FR-009**: On a bulk validation failure, the frontend MUST mark the offending items (by the ids in the reason
  list) so the user can fix the selection.

### Key Entities

- **Selection**: the set of document ids the user has ticked; drives the action bar and the bulk requests.
- **Bulk request**: a de-duplicated list of document ids + (for move) a target folder (or root).
- **Validation failure**: a list of `{ id, reason }` naming every item that blocked the (all-or-nothing) operation.
- **Document / Chunk / Quota**: existing entities — a move changes a document's folder; a delete removes documents
  and their chunks (cascade) and lowers the quota (US-05/08/10).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A bulk move relocates 100% of the selected valid documents in a single request; a bulk delete removes
  100% of them (records + chunks) in one transaction.
- **SC-002**: When any selected item is invalid, **0** documents are changed/deleted and the response lists every
  offending id with a reason (all-or-nothing) in 100% of such cases.
- **SC-003**: A bulk delete decreases the quota by exactly the number of deleted documents, every time.
- **SC-004**: A bulk request containing another session's id is refused and that id is reported as not-found in
  100% of attempts (no existence disclosure).
- **SC-005**: Empty, over-cap, or duplicate id lists are handled deterministically (rejected / de-duplicated) in
  100% of cases.
- **SC-006**: After a successful bulk action, the selection is cleared and the tree + quota reflect the change
  without a page reload, every time.

## Assumptions

- The document + its folder and origin (US-04/10), delete-with-cascade + quota (US-05/08), the target-folder
  session check (US-10 `IFolderReference`), the tree + quota views (US-07), and session isolation (US-01) exist on
  master and are reused.
- Both bulk operations run in a single transaction with validate-all-then-apply semantics; no new persisted entity;
  no migration.
- The list-size cap is configuration-driven (quota-ready narrative). Reasons are stable codes reused from the
  single-item operations where they apply (not-found, read-only, missing target folder).
- The per-id failure-list **response shape and status** (e.g. a ProblemDetails extension vs a result body) is the
  one open design decision — resolved in `/speckit-clarify`.
- Toasts/dialogs use the project's shared UI + design tokens; no native dialogs; ≥360px.
- Out of scope: bulk drag-and-drop, bulk rename, and bulk operations on folders.
