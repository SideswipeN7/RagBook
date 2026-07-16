# Feature Specification: Przenoszenie folderów — z poddrzewem (US-11)

**Feature Branch**: `016-us11-move-folders`

**Created**: 2026-07-15

**Status**: Draft

**Input**: User description: US-11 — drag a folder into another folder (or to the root) together with its whole
subtree, to reorganise the structure without moving files one by one; optimistic UI with rollback, and invalid
targets (the folder's own subtree) refuse the drop.

## Context

Moving a folder carries its entire subtree — subfolders **and** the documents inside them — in one operation. On
the backend it stays cheap thanks to the materialized-path model: re-parent the folder and rewrite the `path`
prefix of the folder **and every descendant** in a single update, inside one transaction; **documents need no
change** (each still points at the same folder). Three rules protect the tree: a folder can't move into itself or
a descendant (cycle), the resulting nesting can't exceed the maximum depth, and a same-named sibling in the target
is a conflict (no auto-merge). This extends US-09 (folders / materialized path), US-10 (the drag-and-drop tree +
optimistic/rollback), US-13 (chat scope, which must follow the moved documents), and US-01 (session isolation).

## Clarifications

### Session 2026-07-15

- Q: Should US-11 folder moves get a keyboard/menu fallback ("Przenieś do…") like US-10 document moves, or ship drag-and-drop only? → A: **Add a folder menu (parity)** — a per-folder "Przenieś do…" menu lists valid target folders (+ Root, **excluding** the folder's own subtree) and invokes the same `moveFolder` action, keeping non-pointer parity with US-10.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Move a folder with its subtree (Priority: P1) 🎯 MVP

The user drags a folder onto another folder; the folder and everything inside it (subfolders + files) appears in
the new location at once, and the move persists.

**Why this priority**: This is the whole feature — reorganising a branch of the tree in one gesture instead of
moving files individually.

**Independent Test**: With `Umowy/2026` (files in both) and a folder `Archiwum`, move `Umowy` into `Archiwum`;
the structure becomes `Archiwum/Umowy/2026`, every descendant's location is updated, and all files show under
their new paths.

**Acceptance Scenarios**:

1. **Given** `Umowy/2026` with files in both folders and a folder `Archiwum`, **When** the user drags `Umowy`
   onto `Archiwum`, **Then** the structure becomes `Archiwum/Umowy/2026`, every descendant folder's path is
   updated, and all files are visible in their new locations.
2. **Given** the move succeeded, **When** the user scopes the chat (US-13) to `Archiwum`, **Then** the scope
   includes the moved documents (they are now within `Archiwum`'s subtree).

---

### User Story 2 - Cycles are impossible (Priority: P1)

A folder cannot be moved into itself or one of its own descendants — the UI won't even highlight such a target
during the drag, and the server refuses it.

**Why this priority**: A cycle would corrupt the tree; it must be impossible both in the gesture and at the API.

**Independent Test**: With `A/B`, attempt to move `A` into `B`; the drag never highlights `B` (a descendant) as a
valid target, and a direct API attempt fails as a circular move.

**Acceptance Scenarios**:

1. **Given** a folder `A` with a subfolder `A/B`, **When** the user tries to move `A` into `B`, **Then** the move
   is refused as a circular move, and `B` (a descendant of `A`) is never highlighted as a valid drop target during
   the drag.

---

### User Story 3 - Depth and name-conflict guards (Priority: P2)

A move that would nest too deep, or collide with a same-named sibling in the target, is refused with a clear
reason.

**Why this priority**: These preserve the tree's invariants (max depth, unique name per parent) that the rest of
the app relies on.

**Independent Test**: Move a subtree so the result exceeds the max depth → refused (too deep). Move a folder into
a target that already has a folder of that name → refused (duplicate name).

**Acceptance Scenarios**:

1. **Given** a subtree whose height + the target's depth would exceed the maximum, **When** the move is attempted,
   **Then** it is refused with a "maximum depth" reason.
2. **Given** the target already contains a folder with the same name, **When** the move is attempted, **Then** it
   is refused with a "duplicate name" reason (no auto-merge).

---

### User Story 4 - Move to the root (Priority: P2)

Dropping a folder onto the root zone makes it (and its subtree) a top-level branch.

**Why this priority**: Reorganising must include promoting a branch back to the top level.

**Independent Test**: Move a nested folder onto the root zone; it becomes a root folder and its descendants'
paths update accordingly.

**Acceptance Scenarios**:

1. **Given** a nested folder, **When** the user drops it onto the root zone, **Then** it becomes a root folder and
   every descendant's path is updated to the new root-based location.

---

### User Story 5 - Optimistic + rollback (Priority: P1)

The moved folder re-nests instantly; if the server rejects the move, the tree snaps back and the reason is shown —
consistent with US-10.

**Why this priority**: Instant feedback with a safe rollback is the interaction contract established in US-10; a
folder move must behave the same.

**Independent Test**: Drive a folder move whose request fails; assert the folder returns to its original parent
and a notice explains why.

**Acceptance Scenarios**:

1. **Given** a folder drag, **When** the server returns an error, **Then** the tree returns to its pre-move state
   and a notice shows the reason.

### Edge Cases

- **Move to the folder's current parent** → a no-op: no request, nothing changes.
- **Concurrent moves of the same folder** → the second operation runs against a now-stale path; the transaction
  validates the prefix under a row lock so it fails cleanly rather than corrupting paths.
- **Another session's folder or target** → not found (404); the move is impossible and rolls back.
- **Move to the root** (no parent) → the folder becomes top-level and every descendant's path is rewritten.
- **A folder's documents** are untouched by the move (they keep pointing at the same folder), but they follow it
  because the folder moved.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST let a user move a folder — with its entire subtree (subfolders + their documents) —
  into a target folder or to the root, by dragging the folder onto a folder node or the root drop-zone.
- **FR-002**: A move MUST re-parent the folder and rewrite the location (path) of the folder **and every
  descendant** atomically (one transaction); a partial move MUST never be observable.
- **FR-003**: The move MUST NOT change any document's owning folder — documents follow their folder unchanged (no
  re-index, no per-file update).
- **FR-004**: The system MUST refuse a **circular** move — a folder cannot move into itself or any of its
  descendants — both by not offering such a target during the drag and by rejecting it at the API.
- **FR-005**: The system MUST refuse a move whose resulting **nesting depth** would exceed the configured maximum,
  with a clear "maximum depth" reason.
- **FR-006**: The system MUST refuse a move when the target already contains a **folder with the same name**
  (case-insensitive), with a "duplicate name" reason — no auto-merge.
- **FR-007**: A move to the folder's **current parent** MUST be a no-op — no request is sent and nothing changes.
- **FR-008**: The move MUST be **session-isolated**: the folder and the target must belong to the current session
  (else not found, 404, never disclosing existence); any bulk path rewrite MUST be constrained to the current
  session.
- **FR-009**: The move MUST be **optimistic** — the folder re-nests immediately — and MUST **roll back** to its
  previous parent with a notice (carrying the reason) if the server rejects it (consistent with US-10).
- **FR-010**: After a successful move, the tree MUST reflect the folder and its subtree in the new location
  (including corrected paths/depths) without a page reload.
- **FR-011**: A per-folder **"Przenieś do…"** menu MUST offer the same move (choose a target folder or the root)
  via the **same** action as drag-and-drop, so a folder move has a non-pointer path (parity with US-10). The menu
  MUST NOT offer the folder itself or any of its descendants as a target.

### Key Entities

- **Folder**: a node in the session's tree with a parent (or none = root) and a materialized location (path) whose
  segments identify its ancestors; it owns subfolders and documents. A move changes its parent and rewrites the
  location of it and its descendants.
- **Subtree**: the moved folder plus all descendant folders (and their documents) that move with it.
- **Target**: the destination — a folder, or the root (no parent) — which must exist in the session, must not be
  the moved folder or a descendant, and must not already contain a same-named folder.
- **Move action**: the single operation both drag-and-drop and the "Przenieś do…" menu invoke — (folder, target
  parent-or-root) → the re-parented folder + rewritten subtree paths.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Moving a folder relocates 100% of its descendants (subfolders + files) to the correct new locations,
  verified by every descendant's path after the move.
- **SC-002**: A moved folder re-nests **immediately** (before the server responds) in 100% of moves; a rejected
  move returns it to its original parent with a reason in 100% of failures.
- **SC-003**: A move never changes any document's owning folder (documents are untouched) — verified before/after.
- **SC-004**: A circular move is impossible in 100% of attempts — never offered as a drag target and always
  refused at the API.
- **SC-005**: A move that would exceed the maximum depth, or collide with a same-named sibling, is refused with the
  correct reason in 100% of such attempts.
- **SC-006**: A cross-session move attempt is refused as not-found in 100% of attempts (no existence disclosure).
- **SC-007**: After a folder moves, scoping the chat to the new ancestor includes the moved documents in 100% of
  cases (the scope follows the subtree).

## Assumptions

- Folders use the materialized-path model with a session-scoped existence check (US-09); the maximum depth is
  configuration-driven; per-parent name uniqueness is enforced at the database boundary.
- The tree view + drag-and-drop + `TreeStore` optimistic/rollback pattern (US-07/US-10) exist on master and are
  extended to folder nodes; folder nesting in the composed tree is by parent, so an optimistic re-parent re-nests
  the whole subtree instantly (paths/depths are corrected by a follow-up refresh on success).
- The move is a single re-parent + one bulk path-prefix rewrite in a transaction; the bulk rewrite is explicitly
  constrained to the current session (a global filter does not apply to a raw bulk update). No new persisted entity.
- Toasts/notices and drag-and-drop use the project's shared UI + design tokens; no native dialogs; ≥360px.
- Out of scope: merging folders of the same name, copying folders, and dragging **multiple** items at once (US-12).
