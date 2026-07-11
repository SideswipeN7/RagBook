# Feature Specification: Folder & Document Tree (Drzewo folderów i lista dokumentów)

**Feature Branch**: `005-us07-tree`

**Created**: 2026-07-11

**Status**: Draft

**Input**: US-07 — A visitor sees their documents inside the folder hierarchy, with the key metadata for
each file (name, size, status, chunk count, upload date), so they can orient themselves in their
knowledge base and manage it. The main view is a tree panel (folders + files) beside the chat panel.
The whole tree — folders and documents — arrives in **one** call so there is no per-folder fan-out.
Depends on US-04 (documents) and US-09 (folders), both shipped. Read-only view: it renders and stays
fresh; the mutating actions (upload/delete/move) live in their own stories and only trigger a refresh
of the shared store. Cross-cutting decisions from `docs/features/README.md` and the constitution apply
— session isolation (cross-session data invisible), config where relevant, one composed response.

## Clarifications

### Session 2026-07-11

Most decisions are fixed by US-07 "Kontekst / decyzje projektowe" and the README, and are not re-opened:

- **One composed response**: the tree (folders + documents for the session) comes from a single read,
  built into the nested tree on the client from the flat lists — no N+1 per folder.
- **Placement & ordering**: root documents (no folder) sit at the top level; ordering is fixed —
  **folders alphabetically (case-insensitive), documents by upload date descending** — not user-configurable.
- **Expansion state** (which folders are open) is **browser-session UI state**, not persisted server data.
- **The Demo section** (US-03) is rendered **separately and read-only**; US-03 is not built, so it is a
  forward-looking, empty placeholder here.
- **Read-only scope**: upload/delete/move actions belong to their own stories; US-07 only renders and
  exposes the shared refresh hook.

Three points genuinely needed product input and were resolved this session:

- Q: How is the failed-document reason handled in US-07, given the field belongs to US-06? → A: **Add a
  nullable `FailureReason` column to the document now (forward-looking)** — US-07 displays it, US-06
  populates it; a failed document without a reason shows a generic message.
- Q: What is the human-readable file-size format? → A: **Decimal B / KB / MB, one decimal place** (1 MB =
  1,000,000 bytes, matching the US-05 quota convention) — e.g. `512 B`, `900 KB`, `12.3 MB`.
- Q: How is the unified folders+documents tree built on the client? → A: **Use the `@angular/cdk`
  `cdk-tree`** (nested tree control) — as the story's context note specifies — rather than extending the
  existing recursive component.

Remaining implementation details (DTO field casing, the nested data source shape, the size-formatting
helper) are deferred to `plan.md`.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - See the knowledge base as one tree (Priority: P1)

A visitor with folders (e.g. `A`, `A/B`) and documents in `A`, `A/B`, and the root opens the app and
sees a single tree: folders nested by hierarchy with their documents inside, and root documents at the
top level. Folders expand and collapse; the open/closed state is remembered while the browser session
lasts.

**Why this priority**: This is the feature — a coherent view of everything the visitor has is the
prerequisite for managing it (opening, deleting, moving, and eventually asking questions about it).

**Independent Test**: Seed folders `A`, `A/B` and documents in each level, open the view, and confirm
the tree mirrors the hierarchy with documents under the right folders and root documents at the top;
collapse a folder, navigate within the app, and confirm it is still collapsed.

**Acceptance Scenarios**:

1. **Given** folders `A`, `A/B` and documents in `A`, `A/B`, and the root, **When** the visitor opens
   the app, **Then** the tree reflects the hierarchy, folders can be expanded/collapsed, and root
   documents appear at the top level.
2. **Given** a folder was collapsed, **When** the visitor navigates elsewhere and back within the same
   browser session, **Then** that folder is still collapsed (expansion state is remembered for the session).
3. **Given** an empty folder, **When** it is shown, **Then** it is still expandable and reveals an
   "empty folder" indication.

---

### User Story 2 - Read each document's key metadata at a glance (Priority: P1)

For every document the visitor sees its name, a human-readable size, a status badge, the chunk count,
and the upload date. A processing document shows a spinner; a failed one shows an error indicator with
the failure reason on hover; a ready one shows its chunk count.

**Why this priority**: The metadata is what makes the tree a management surface rather than a bare list
— the visitor needs to know what is ready, what is still processing, what failed and why, and how big
each item is.

**Independent Test**: Seed documents in each status and confirm each row shows the correct name, size,
status badge, chunk count, and date; the processing row shows a spinner and the failed row shows the
failure reason on hover.

**Acceptance Scenarios**:

1. **Given** a ready document, **When** the visitor looks at its row, **Then** it shows the name, a
   human-readable size, a "ready" badge, the chunk count, and the upload date.
2. **Given** a processing document, **When** the visitor looks at its row, **Then** it shows a
   processing indicator (spinner) instead of a chunk count.
3. **Given** a failed document, **When** the visitor hovers its status, **Then** it shows an error
   indicator and reveals the failure reason.
4. **Given** a document with a very long name, **When** it is shown, **Then** the name is truncated with
   the full name available on hover.

---

### User Story 3 - Helpful empty state for a fresh session (Priority: P1)

A visitor with no folders and no documents sees an inviting empty state — a call to action to upload
their first document, and a pointer to the demo mode — rather than a blank panel.

**Why this priority**: First impressions; a new visitor must know what to do next.

**Independent Test**: Open the view for a brand-new session and confirm the empty state with the upload
call to action and the demo-mode pointer appears (no folders/documents rendered).

**Acceptance Scenarios**:

1. **Given** a new session with no folders and no documents, **When** the visitor opens the view,
   **Then** an empty state with an "upload your first document" call to action and a demo-mode pointer
   is shown.

---

### User Story 4 - The tree stays fresh without a reload (Priority: P1)

When an upload, deletion, or move completes (in its own story), the tree reflects the change without a
full page reload, because it reads from one shared, refreshable source.

**Why this priority**: A management view that goes stale after every action would force reloads and
break the single-view experience.

**Independent Test**: With the tree open, complete an upload (or delete), and confirm the tree updates
to include (or drop) the item without reloading the page.

**Acceptance Scenarios**:

1. **Given** the tree is open, **When** an upload completes, **Then** the new document appears in the
   tree without a page reload.
2. **Given** the tree is open, **When** a deletion completes, **Then** the removed item disappears
   without a page reload.

---

### Edge Cases

- **Empty folder** → rendered as expandable, revealing an "empty folder" note when opened.
- **Very long file (or folder) name** → truncated with an ellipsis; the full name is available on hover.
- **Only root documents, no folders** → all documents render at the top level.
- **Failed document without a recorded reason** (until US-06 populates it) → the error indicator shows a
  generic "processing failed" message rather than an empty tooltip.
- **Cross-session data** → a folder or document owned by another session never appears in this session's
  tree.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST return the current session's folders and documents for the tree in **one**
  read, with no per-folder follow-up reads (no N+1), so the whole view loads from a single request.
- **FR-002**: The composed response MUST include, for each folder: identity, parent, name, and depth;
  and for each document: identity, owning folder (or none for root), name, size, status, chunk count,
  upload date, and — when present — the failure reason.
- **FR-003**: The view MUST render the folders as a nested tree with each document placed under its
  folder, and root documents (no folder) at the top level.
- **FR-004**: Folders MUST be expandable/collapsible, and the expansion state MUST persist for the
  duration of the browser session as **UI state** (never as server-side data).
- **FR-005**: Each document row MUST display the name, a **human-readable size** (decimal `B`/`KB`/`MB`,
  one decimal place; 1 MB = 1,000,000 bytes — matching the quota convention), a status badge, the chunk
  count, and the upload date.
- **FR-006**: Status MUST be conveyed distinctly: **processing** → a spinner (no chunk count yet);
  **failed** → an error indicator revealing the failure reason on hover; **ready** → its chunk count.
- **FR-007**: When the session has no folders and no documents, the view MUST show an **empty state**
  with an "upload your first document" call to action and a pointer to demo mode.
- **FR-008**: Ordering MUST be fixed and not user-configurable: **folders alphabetically
  (case-insensitive)**; **documents by upload date, most recent first**.
- **FR-009**: The tree MUST update to reflect a completed upload, deletion, or move **without a full
  page reload**, by reading from a single shared, refreshable source.
- **FR-010**: A long name MUST be truncated in the row with the full name available on hover.
- **FR-011**: An empty folder MUST still be expandable and reveal an "empty folder" indication.
- **FR-012**: The tree MUST show **only** the current session's folders and documents; data owned by
  another session MUST never appear (isolation inherited from US-01).
- **FR-013**: The **Demo** area MUST be rendered as a **separate, read-only** section; until US-03 exists
  it is an empty placeholder and MUST NOT mix demo content into the session's own tree.

### Key Entities *(include if feature involves data)*

- **Tree (composed view)**: The session's complete picture for one read — its folders and its documents
  together. It is a read model assembled from the existing folder and document data; it owns no new
  stored state.
- **Folder node**: A folder's identity, parent, name, and depth (from US-09), used to place items in the
  hierarchy.
- **Document node**: A document's display metadata — identity, owning folder (or root), name, size,
  status, chunk count, upload date, and optional failure reason — projected from the US-04 document.
- **Failure reason**: The human-readable explanation shown for a failed document, stored as a **nullable
  `FailureReason` field on the document** (added by US-07, forward-looking). It is **populated by US-06**
  (background processing); US-07 only surfaces it and, when absent, shows a generic "processing failed".

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: At the maximum modelled state (10 documents, folders up to 3 levels deep) the entire view
  loads from **one** request — 0 per-folder follow-up requests.
- **SC-002**: The rendered tree matches the underlying hierarchy — every document under its folder and
  every root document at the top level — in 100% of cases.
- **SC-003**: For each document, all five metadata items (name, human-readable size, status, chunk
  count, date) are displayed correctly for each of the three statuses.
- **SC-004**: A brand-new session (no data) shows the empty state with the upload call to action and the
  demo pointer in 100% of cases.
- **SC-005**: A folder's expansion state is preserved across in-app navigation within the same browser
  session in 100% of cases.
- **SC-006**: After an upload or a deletion completes, the tree reflects the change with **zero** full
  page reloads.
- **SC-007**: A document or folder owned by another session appears in this session's tree in **0%** of
  cases.

## Assumptions

- **US-04 and US-09 are in place**: the document metadata (name, folder, size, status, chunk count,
  upload date) and the folder hierarchy already exist and are reused; US-07 adds a read/compose surface
  and the view, not new domain rules.
- **Failure reason is forward-looking**: a **nullable `FailureReason` field is added to the document now**
  and surfaced by US-07, but **filled by US-06**; until then failed documents show a generic reason.
- **The tree is read-only here**: upload lives in US-04, delete in US-08, move in US-10/US-11; US-07
  provides only the shared, refreshable source those actions call to update the view.
- **Human-readable size** uses the same decimal-megabyte convention as the quota (1 MB = 1,000,000
  bytes), shown with a sensible unit (B/KB/MB); the exact rounding is confirmed via clarification.
- **The existing folders-only tree component** (US-09) is superseded by this unified folders+documents
  tree, built with the **`@angular/cdk` `cdk-tree`** (a new frontend dependency); the folder context
  actions it carried are re-provided in the new tree.
- **Demo mode (US-03) is not built**: its section is a labelled, empty, read-only placeholder and does
  not affect the session's own tree.
