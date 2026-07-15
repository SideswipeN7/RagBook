# Feature Specification: Przenoszenie plików — drag & drop (US-10)

**Feature Branch**: `015-us10-drag-drop`

**Created**: 2026-07-15

**Status**: Draft

**Input**: User description: US-10 — drag a document onto a folder (or the root zone) in the tree to move it, with an instant UI response (optimistic update + rollback on error), plus a keyboard/menu fallback ("Przenieś do…").

## Context

Moving a document is, on the backend, a one-line change of its owning folder — the value of this story is the
**interface**: dragging a document row onto a folder node (or a root drop-zone), an **optimistic** move that
appears instantly and **rolls back** with a toast if the server rejects it, clear drop-target highlighting, and a
**context-menu fallback** so drag-and-drop is never the only path (accessibility). Changing a document's folder
does **not** touch the vector index — chunks are untouched — because a folder is just an attribute of the
document. This builds on US-07 (the tree), US-09 (folders), US-04 (the document + its folder), and US-01
(session isolation).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Drag a document onto a folder (Priority: P1) 🎯 MVP

The user drags a document from the root onto a folder node; it appears there **immediately**, and the move is
saved in the background.

**Why this priority**: This is the core interaction — a fast, direct way to organise documents; without it the
story delivers nothing.

**Independent Test**: With a document in the root and a folder "Umowy", drag the document onto "Umowy" and drop;
the UI shows it under "Umowy" at once, and the move is persisted.

**Acceptance Scenarios**:

1. **Given** a document in the root and a folder "Umowy", **When** the user drops the document onto the "Umowy"
   node, **Then** the UI immediately shows it under "Umowy" (optimistic), the move is sent in the background, and
   on success the state stays.
2. **Given** a successful move, **When** the tree is reloaded, **Then** the document is under its new folder (the
   move persisted).

---

### User Story 2 - Rollback on failure (Priority: P1)

If the server rejects the move (e.g. the target folder was deleted meanwhile), the document snaps back to where it
was and the user is told why.

**Why this priority**: An optimistic UI without a rollback would silently diverge from the real state — the
rollback is what makes the optimism safe and trustworthy.

**Independent Test**: Drive a move whose request fails; assert the document returns to its original folder and a
toast with the reason appears.

**Acceptance Scenarios**:

1. **Given** an optimistic move in flight, **When** the server responds with an error, **Then** the document
   returns visually to its previous folder and a toast shows the reason.

---

### User Story 3 - Drop-target feedback (Priority: P2)

While dragging, valid targets highlight and invalid ones stay inert, so the user knows where a drop will land.

**Why this priority**: Without target feedback, drops feel like guesswork; it turns the interaction from fiddly
into obvious.

**Independent Test**: Start a drag; a folder node and the root zone highlight when hovered; a demo section or the
dragged document itself do not react.

**Acceptance Scenarios**:

1. **Given** a drag in progress, **When** the cursor is over a valid target (a folder node or the root zone),
   **Then** that target is highlighted; invalid targets (the demo section, the document itself) do not react.

---

### User Story 4 - Move to the root (Priority: P2)

Dropping a document onto the root zone moves it out of any folder.

**Why this priority**: Organising isn't only nesting — the user must be able to pull a document back to the top.

**Independent Test**: Drag a document from inside a folder onto the root zone; it becomes a root document (no
folder).

**Acceptance Scenarios**:

1. **Given** a document inside a folder, **When** the user drops it onto the root zone, **Then** it becomes a root
   document (no owning folder).

---

### User Story 5 - Keyboard/menu fallback (Priority: P1)

A user not using drag-and-drop can move a document from its context menu — "Przenieś do…" — with the same effect.

**Why this priority**: Drag-and-drop must not be the **only** path (accessibility); the menu is the required
equivalent.

**Independent Test**: From a document's context menu, choose "Przenieś do…" and pick a folder; the document moves
exactly as a drop would (same underlying action).

**Acceptance Scenarios**:

1. **Given** a document's context menu, **When** the user chooses "Przenieś do…" and selects a target folder (or
   the root), **Then** the document moves identically to a drag-and-drop, via the same move action.

### Edge Cases

- **Drop onto the folder the document is already in** → a no-op: no move is sent, nothing changes.
- **Dragging while the document is Processing** → allowed; the folder is metadata and does not affect the
  processing pipeline.
- **Another session's document or target folder** → not found (404); the move is impossible and rolls back.
- **Target folder deleted between grabbing and dropping** → the move fails → rollback + toast (US Story 2).
- **A read-only (demo) document** → the move is refused with a read-only message and rolls back; demo documents
  cannot be reorganised.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST let a user move a document to a **target folder** or to the **root** (no folder) by
  dragging its row onto a folder node or the root drop-zone.
- **FR-002**: The move MUST be **optimistic** — the document appears in the target immediately — and MUST **roll
  back** to its previous location with a toast (carrying the reason) if the server rejects it.
- **FR-003**: While dragging, **valid** drop targets (folder nodes, the root zone) MUST be highlighted and
  **invalid** ones (the dragged document itself, a demo/read-only section) MUST NOT react.
- **FR-004**: A move MUST be persisted by changing only the document's owning folder; it MUST NOT alter the
  document's indexed content (its chunks/vectors are untouched).
- **FR-005**: The move MUST validate: the document belongs to the current session (else **not found**), the target
  folder — when not the root — exists in the current session (else **folder not found**), and a **read-only
  (demo)** document is **refused**; another session's document/folder is **not found** (404, no disclosure).
- **FR-006**: Dropping a document onto the folder it already belongs to MUST be a **no-op** — no request is sent
  and nothing changes.
- **FR-007**: A document that is still **Processing** MUST be movable (the folder change does not affect
  processing).
- **FR-008**: A **context-menu "Przenieś do…"** action MUST offer the same move (choose a folder or the root) via
  the **same** underlying action as drag-and-drop, so drag-and-drop is never the only path.
- **FR-009**: After a successful move, the tree MUST reflect the document under its new location without a page
  reload.

### Key Entities

- **Document**: the movable item; it has an **owning folder** (a folder, or none = root) and an **origin**
  (user-uploaded vs. read-only demo). A move changes only the owning folder.
- **Folder**: a drop target; must exist in the current session to receive a document (US-09).
- **Root zone**: the drop target representing "no folder" (owning folder = none).
- **Move action**: the single operation both drag-and-drop and the "Przenieś do…" menu invoke — (document, target
  folder-or-root) → the document's new folder.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A dropped document appears in its target **immediately** (before the server responds) in 100% of
  moves.
- **SC-002**: A rejected move returns the document to its original location and shows a reason in 100% of failures
  (no silent divergence from the server state).
- **SC-003**: A move changes only the document's folder — its indexed content is unchanged — in 100% of moves
  (verified by the chunk count/content being identical before and after).
- **SC-004**: Every move achievable by drag-and-drop is also achievable via the context menu (100% parity of the
  underlying action).
- **SC-005**: Dropping onto the current folder issues **zero** move requests.
- **SC-006**: A cross-session move attempt is refused as not-found in 100% of attempts (no existence disclosure).

## Assumptions

- The tree view (US-07), folders + their session-scoped existence check (US-09), the document with its folder and
  origin (US-04), and session isolation (US-01, the global query filter) exist on master and are reused.
- The move is a single small write (change the owning folder); no new persisted entity, no migration.
- Toasts and drag-and-drop use the project's shared UI + design tokens; no native dialogs (`window.confirm`/
  `alert`), and every view works at ≥360px.
- Demo documents (read-only origin) come from US-03 (not yet built); the read-only guard is implemented now so the
  move is complete, and is tested by seeding a demo-origin document.
- Out of scope: dragging **multiple** documents at once (bulk has its own UX — US-12), reordering documents within
  a folder, and moving **folders** with their subtree (US-11).
