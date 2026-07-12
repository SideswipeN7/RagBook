# Feature Specification: Delete Document (Usuwanie dokumentu)

**Feature Branch**: `007-us08-delete`

**Created**: 2026-07-11

**Status**: Draft

**Input**: US-08 — A visitor deletes a document (together with its index) to free quota space and keep
their knowledge base tidy. Deletion is **permanent** (hard delete, no trash) and asks for **confirmation**
in the UI. The document's chunks are removed by a database **cascade** (single source of consistency); the
binary is removed from storage **best-effort after** the database transaction commits — an orphaned blob
on a storage error is an accepted MVP trade-off (logged). Depends on US-04 (upload/storage pointer),
US-05 (quota), US-07 (tree/row), and US-06 (chunks + cascade FK). Cross-cutting decisions from
`docs/features/README.md` and the constitution apply — session isolation (another session's document →
404), errors as `Result<T>` → ProblemDetails with a stable code.

## Clarifications

### Session 2026-07-11

All material decisions are fixed by US-08 "Kontekst / decyzje projektowe", the README, and the existing
US-04/05/06/07 code, and are therefore not re-opened:

- **Hard delete, no trash**; a UI **confirmation** is required (reusing the same inline-confirm pattern as
  folder delete — the app has no shared modal library yet, so no native `window.confirm`).
- **Chunks cascade at the database** via the `documents → chunks` FK `ON DELETE CASCADE` (added in US-06).
- **Order**: database first (one transaction: delete the row → chunks cascade → commit), **then** a
  **best-effort** blob delete via the storage abstraction; a storage failure is logged and does **not**
  fail the delete (an orphaned blob is the accepted trade-off).
- **Quota** (US-05) counts documents, so deleting the row frees the slot; the counter re-reads on refresh.
- **Isolation**: a document owned by another session is invisible, so its id deletes as **not-found** (404).

No ambiguities required product input; implementation details (the exact endpoint shape, the confirm
control's placement) are deferred to `plan.md`.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Delete a document with confirmation (Priority: P1)

A visitor clicks "Delete" on a document row, confirms in a dialog, and the document — with its whole
index — disappears from the tree while the quota counter drops immediately, all without a page reload.

**Why this priority**: Removing documents is the core of the story — it is how a visitor frees quota and
keeps the base tidy; without it the quota is a dead end.

**Independent Test**: With a document present, invoke delete and confirm; verify the document row is gone
from the tree, the quota counter has decreased, and the document (and its chunks) no longer exist.

**Acceptance Scenarios**:

1. **Given** a document in the list, **When** the visitor clicks "Delete" and confirms, **Then** the
   document record and all its chunks are removed, the row disappears from the tree, and the quota
   decreases immediately — no page reload.
2. **Given** the confirmation dialog is shown, **When** the visitor cancels, **Then** nothing is deleted.

---

### User Story 2 - Deleting a document removes its whole index (Priority: P1)

Deleting a ready document with many chunks leaves **no** chunk behind for that document — the index is
gone in one consistent step.

**Why this priority**: A document whose row is gone but whose chunks linger would corrupt retrieval and
leak content; the index must go with the document.

**Independent Test**: Index a document (N chunks), delete it, and confirm zero chunks remain with its id.

**Acceptance Scenarios**:

1. **Given** a ready document with N chunks, **When** it is deleted, **Then** there is no chunk with its
   document id (the chunks cascade with the document).

---

### User Story 3 - Delete a document while it is processing (Priority: P1)

A visitor deletes a document that is still processing. The deletion succeeds; the background worker,
reaching the point of saving, finds the record gone and stops quietly.

**Why this priority**: Documents can be deleted at any time; deletion must not be blocked by in-flight
processing, and processing must not resurrect a deleted document.

**Independent Test**: Start with a processing document, delete it, then run the processing step; confirm
the document stays deleted and no chunks are written, with no error surfaced.

**Acceptance Scenarios**:

1. **Given** a processing document, **When** the visitor deletes it, **Then** the delete succeeds.
2. **Given** the document was deleted mid-processing, **When** the background worker reaches its save
   point, **Then** it stops quietly (no record to update, no chunks written, no error).

---

### User Story 4 - Only the owner can delete (Priority: P1)

Deleting by the id of a document owned by another session behaves as if it does not exist — 404, and that
document is untouched.

**Why this priority**: Isolation is a foundational guarantee; one visitor must never be able to delete (or
even confirm the existence of) another's document.

**Independent Test**: Create a document in session A; attempt to delete it as session B; confirm 404 and
that A's document still exists.

**Acceptance Scenarios**:

1. **Given** a document owned by session A, **When** session B deletes it by id, **Then** the response is
   404 (not-found) and A's document and index are unchanged.

---

### User Story 5 - A citation to a deleted document degrades gracefully (Priority: P1, forward-looking)

A historical chat answer cites a document that has since been deleted. Clicking the citation shows a
"document has been deleted" state instead of an error.

**Why this priority**: Deletions must not turn past answers into broken links; the citation must fail
soft. (Chat/citations are not built yet — US-14/16 — so this is the **behaviour to honour** when they
arrive; US-08 only guarantees the delete makes the source resolvably absent, i.e. 404, not a crash.)

**Independent Test**: Resolving a deleted document by id returns a clean not-found (404), so a future
citation view can render "document deleted" rather than an error.

**Acceptance Scenarios**:

1. **Given** a deleted document, **When** it is resolved by id (as a future citation would), **Then** the
   result is a clean not-found (404), not a server error — so the UI can show "document has been deleted".

---

### Edge Cases

- **Double-click / concurrent delete** → the second delete of the same id returns 404; the UI treats it as
  already-done (idempotent from the visitor's perspective).
- **Storage failure on blob delete** → the document (and its index) are still deleted; the orphaned blob is
  logged, not surfaced as an error.
- **Delete a document that has no stored blob** (e.g. a minimal/edge record) → the database delete still
  succeeds; the (absent) blob delete is a no-op.
- **Delete during processing** → handled: the worker aborts quietly when the record is gone (see US3).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Deleting a document MUST require an explicit **confirmation** in the UI before it happens
  (never a native browser dialog); cancelling MUST delete nothing.
- **FR-002**: On confirmation the system MUST **permanently** delete the document record and its entire
  index (no trash/soft-delete).
- **FR-003**: A document's chunks MUST be removed by the **database cascade** (the `documents → chunks`
  foreign key), not by application-level chunk deletion — one source of consistency.
- **FR-004**: The system MUST delete the **database record first** (in a transaction, with chunks
  cascading), and only **after** commit attempt a **best-effort** removal of the binary from storage; a
  storage failure MUST be logged and MUST NOT fail the delete (an orphaned blob is accepted).
- **FR-005**: After a successful delete the UI MUST reflect it **without a page reload**: the document
  disappears from the tree and the **quota counter decreases immediately**.
- **FR-006**: Deleting a document owned by **another session** MUST return **404 (not-found)** and leave
  that document untouched (isolation inherited from US-01) — never 403.
- **FR-007**: Deletion MUST be **idempotent from the visitor's perspective**: deleting an already-deleted
  (or unknown) id returns 404, which the UI treats as already-done.
- **FR-008**: Deleting a document that is still **processing** MUST succeed; the background worker MUST
  stop **quietly** if the record is gone when it reaches its save point (no chunks written, no error).
- **FR-009**: Delete failures MUST be returned through the standard `Result` → ProblemDetails channel with
  a stable code (`document.not_found`), never a naked 500.
- **FR-010** *(forward-looking)*: Resolving a deleted document by id MUST return a clean **404** (not a
  server error), so a future citation view (US-14/16) can render "document has been deleted" instead of an
  error. US-08 does not build the chat/citation UI.

### Key Entities *(include if feature involves data)*

- **Document (deleted)**: The US-04 session-owned document; deleting it is the subject of this story. It
  carries the storage pointer used for the best-effort blob cleanup.
- **Chunk (cascaded)**: The US-06 index rows; removed automatically by the database when their document is
  deleted (never touched by application code here).
- **Stored blob (best-effort removed)**: The binary in the storage abstraction; removed after the DB
  commit, tolerating failure (logged, orphan accepted).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Deleting a document removes its record and **100%** of its chunks (zero chunks remain with
  its id).
- **SC-002**: After a delete, the quota counter reflects the freed slot **immediately**, with **zero** page
  reloads.
- **SC-003**: After a delete, the document is gone from the tree **without** a page reload.
- **SC-004**: A cross-session delete returns 404 in **100%** of attempts and leaves the target document and
  its index unchanged (0% cross-session deletion).
- **SC-005**: Deleting a processing document leaves **zero** chunks and surfaces **no** error, even when the
  worker runs afterward (it aborts quietly).
- **SC-006**: A storage failure during blob cleanup does **not** fail the delete — the record and index are
  still gone in 100% of such cases (the orphaned blob is logged).

## Assumptions

- **US-04, US-05, US-06, US-07 are in place**: the document + storage pointer, the quota, the chunks with
  the cascade FK, and the tree/row all exist and are reused. US-08 adds the delete command/endpoint and the
  row's delete action; it introduces no new schema (the cascade FK already exists from US-06).
- **Confirmation reuses the inline-confirm pattern** already used for folder delete (US-09) — the app has
  no shared modal library yet; a proper shared confirm dialog is future UI work. No native `window.confirm`.
- **The worker's quiet-abort is already implemented** (US-06 reads the target and stops when it is gone);
  US-08 relies on it for AC-3, and asserts it.
- **AC-5 is forward-looking**: chat and citations (US-14/16) are not built; US-08 only guarantees a clean
  404 for a deleted document so the future citation view can degrade gracefully — it builds no chat UI.
- **Out of scope**: trash/restore, deleting folders that contain items (US-09 blocks non-empty), and bulk
  delete (US-12).
