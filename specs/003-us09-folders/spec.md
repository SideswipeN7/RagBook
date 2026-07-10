# Feature Specification: Folder CRUD (Hierarchia folderów)

**Feature Branch**: `003-us09-folders`

**Created**: 2026-07-10

**Status**: Draft

**Input**: US-09 — CRUD folderów (hierarchia). A visitor organises their documents into a
tree of folders they can create, rename, and delete — including nested folders up to three
levels deep, each folder living in exactly one parent, names unique within a parent. A
foundation story for the folder epic (P1) in milestone M1; depends only on US-01 (session
isolation). Binding cross-cutting decisions from `docs/features/README.md` ("Decyzje
przekrojowe") and the constitution apply — materialized-path hierarchy, `Result<T>` →
ProblemDetails with stable codes, cross-session access → 404, config-driven limits with
zero magic numbers.

## Clarifications

### Session 2026-07-10

A structured ambiguity scan (functional scope, domain/data, UX flow, non-functional,
integration, edge cases, constraints, terminology, completion signals) was run against this
spec. Most material decisions are already fixed by US-09 "Kontekst / decyzje projektowe",
the README "Decyzje przekrojowe", and the constitution, and are therefore not re-opened —

- Hierarchy is represented by a **materialized path** (segments are folder IDs); subtree
  scope is a prefix match, not a recursive query — fixed by the story + README.
- Maximum depth is **3 levels**, validated by the number of path segments — fixed by the story.
- Names are **unique within a parent** (root counts as a distinct parent) — fixed by the story.
- Only **empty** folders may be deleted (no files and no subfolders); cascade delete is
  future work — fixed by the story.
- Rename does not move a folder and does not rewrite descendants' paths (segments are IDs,
  not names) — fixed by the story.
- Errors flow through `Result<T>` → ProblemDetails with a stable `code`; cross-session access
  returns 404 — fixed by README + constitution §II/§III.

Three ambiguities were resolved by product input this session:

- Q: Folder-name uniqueness within a parent — case-sensitive or case-insensitive? → A:
  **Case-insensitive** ("Umowy" and "umowy" in the same parent collide).
- Q: How are leading/trailing whitespace handled on create and rename? → A: **Trim
  leading/trailing whitespace; a name that is empty after trimming is rejected as invalid.**
- Q: Default ordering of sibling folders in the tree? → A: **Alphabetical, case-insensitive
  (locale-aware).**

Implementation-level decisions are deferred to `plan.md` (not product input): the exact
path-segment format and index strategy (`text_pattern_ops`), the case-insensitive uniqueness
mechanism (e.g. `citext` vs. a `LOWER(name)` functional unique index), the partial unique
index for root folders (`parent_id IS NULL`), and the folder-tree UI component/store structure.

**One dependency-sequencing note is recorded rather than escalated** (it does not change what
US-09 builds): AC-5 blocks deletion of a folder that still contains **files**, but the file
concept and its document table arrive in US-04. US-09 owns the emptiness rule and the
subfolder check it can prove today; the "contains files" arm of the rule is wired through a
forward-looking seam and validated end-to-end once US-04 lands. This mirrors how US-05 wired
the upload/delete seam ahead of US-04/US-08.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Create folders and nest them (Priority: P1)

A visitor in the document tree creates a top-level folder, then creates another folder inside
it, building a hierarchy that mirrors how they organise their work. Each new folder appears in
the tree under its parent.

**Why this priority**: Creation is the entry point of the whole folder epic — without it there
is no tree to rename, delete, fill, or navigate. Every other scenario in this story and the
upload/move stories depends on folders existing.

**Independent Test**: Create a root folder "Umowy", then a child folder "2026" inside it, and
observe both persisted under the current session with the child correctly placed beneath the
parent in the returned tree.

**Acceptance Scenarios**:

1. **Given** a visitor viewing their (possibly empty) folder tree, **When** they create a folder
   "Umowy" at the root and then a folder "2026" inside "Umowy", **Then** both folders are created
   and the tree shows "2026" nested under "Umowy".
2. **Given** two independent sessions A and B, **When** each creates folders, **Then** each sees
   only its own folders — one session's tree never shows the other's folders.

---

### User Story 2 - Depth limit enforced (Priority: P1)

A visitor who has already nested folders three levels deep tries to create a folder inside the
deepest one. The system refuses, tells them the nesting limit is reached, and the UI does not
even offer a "New folder" action at that depth.

**Why this priority**: The three-level ceiling is a hard product constraint that shapes the data
model and every later folder operation (move, bulk). A limit that can be exceeded is not a limit,
and deeper trees would break the path/scope assumptions the RAG scope story relies on.

**Independent Test**: Seed a chain of folders at the maximum depth, attempt to create a child of
the deepest folder, and observe a stable "max depth exceeded" failure with nothing persisted.

**Acceptance Scenarios**:

1. **Given** a folder at the third (deepest allowed) level, **When** the visitor attempts to
   create a subfolder inside it, **Then** the system rejects it with a stable "max depth exceeded"
   error code and nothing is persisted.
2. **Given** the same third-level folder, **When** it is shown in the UI, **Then** the "New folder"
   action is not offered for it, so the limit is communicated before the user attempts the action.

---

### User Story 3 - Names unique within a parent (Priority: P1)

A visitor tries to create a second folder with a name that already exists in the same parent. The
system refuses the duplicate, but allows the same name to be used under a different parent.

**Why this priority**: Duplicate names in one parent make the tree ambiguous to read and to
address; scoping folders unique per-parent (not globally) is what lets the same natural name
("2026", "Faktury") recur across branches.

**Independent Test**: Create "Umowy" at the root, attempt a second "Umowy" at the root and observe
a duplicate-name failure; then create "Umowy" inside a different folder and observe it succeeds.

**Acceptance Scenarios**:

1. **Given** a folder "Umowy" at the root, **When** the visitor creates another "Umowy" at the
   root, **Then** the system rejects it with a stable "duplicate folder name" error code and
   nothing is persisted.
2. **Given** a folder "Umowy" at the root, **When** the visitor creates "Umowy" inside a *different*
   parent, **Then** it is admitted — the name is only required to be unique within its own parent.
3. **Given** two identically named folder creations racing in the same parent (two tabs, a
   double-submit), **When** they arrive concurrently, **Then** at most one is admitted and the other
   fails with the same duplicate-name code — the uniqueness guarantee holds under concurrency.

---

### User Story 4 - Rename a folder (Priority: P1)

A visitor renames a folder that already contains files and subfolders. The name changes everywhere
it is shown; the folder does not move and its contents are undisturbed. The same
uniqueness-within-parent rule applies to the new name.

**Why this priority**: Reorganising labels without moving or losing content is core to keeping a
tree useful over time; because folder identity is stable across a rename, descendants are
untouched and the operation is cheap.

**Independent Test**: Rename a non-empty folder and observe the new name reflected, the folder's
position unchanged, and every descendant still reachable in the same place.

**Acceptance Scenarios**:

1. **Given** a folder with files and subfolders, **When** the visitor renames it, **Then** the new
   name is shown, the folder keeps its place in the tree, and its descendants remain exactly where
   they were.
2. **Given** a folder "Umowy" alongside a sibling "Faktury" in the same parent, **When** the visitor
   renames "Faktury" to "Umowy", **Then** the system rejects it with the duplicate-name error code.
3. **Given** a rename to an invalid name (empty, too long, or containing a path separator), **When**
   it is submitted, **Then** the system rejects it with the invalid-name error code.

---

### User Story 5 - Delete an empty folder, keep a non-empty one (Priority: P1)

A visitor deletes a folder they no longer need. If it is empty the deletion goes through after a
confirmation. If it still holds files or subfolders the system refuses and tells them to empty or
move the contents first — nothing is deleted implicitly.

**Why this priority**: Safe deletion prevents accidental loss of nested content; blocking non-empty
deletes is the deliberate MVP guardrail (cascade delete is explicitly deferred), so users can never
lose a subtree with one click.

**Independent Test**: Delete an empty folder and observe it removed; attempt to delete a folder
containing a subfolder (and, once US-04 lands, a file) and observe a stable "folder not empty"
failure with nothing removed.

**Acceptance Scenarios**:

1. **Given** an empty folder, **When** the visitor confirms deletion, **Then** the folder is removed
   from the tree.
2. **Given** a folder containing a subfolder or a file, **When** the visitor attempts to delete it,
   **Then** the system rejects it with a stable "folder not empty" error code and nothing is removed,
   surfacing a message to delete or move the contents first.

---

### Edge Cases

- **Name validation** → a name that is empty *after trimming leading/trailing whitespace* (including
  whitespace-only), longer than the configured maximum (default 100 characters), or contains the
  path separator `/` is rejected with the invalid-name code, on both create and rename.
- **Case-only difference** → creating "umowy" beside an existing "Umowy" in the same parent is a
  duplicate (case-insensitive) and rejected; the same names may coexist under different parents.
- **Whitespace-padded duplicate** → "  Umowy  " is trimmed to "Umowy" before the uniqueness check,
  so it collides with an existing "Umowy" rather than creating a second, visually-identical folder.
- **Concurrent duplicate creates** → two creates of the same name in the same parent race; the
  database uniqueness guarantee admits at most one, and the loser is mapped to the duplicate-name
  code (not a naked 500).
- **Delete-after-last-child race** → a folder is deleted in one tab just as its last child is being
  removed in another; the emptiness check is evaluated transactionally so a folder is never left
  dangling or wrongly deleted while non-empty.
- **Cross-session access** → creating, renaming, or deleting against a folder ID that belongs to a
  different session behaves as if the folder does not exist (404), never revealing its existence.
- **Root-level uniqueness** → the uniqueness rule treats "no parent" (root) as a distinct scope, so
  two root folders may not share a name while a root folder and a nested folder may.
- **Rename to the same name** → renaming a folder to its current name is a no-op success, not a
  duplicate-name failure.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Users MUST be able to create a folder either at the root or inside an existing folder
  they own; the created folder is scoped to the current session and placed under the chosen parent.
- **FR-002**: The system MUST represent the folder hierarchy by a **materialized path whose segments
  are folder identifiers**, so that a folder's subtree is addressable by a prefix match without a
  recursive query. (Binding decision — not to be re-opened in planning.)
- **FR-003**: The system MUST reject creating a folder deeper than the configured maximum nesting
  depth (default **3 levels**), returning a stable "max depth exceeded" error code without
  persisting anything; depth is determined from the parent's path.
- **FR-004**: The system MUST enforce that a folder name is **unique within its parent** (root
  counted as a distinct parent), compared **case-insensitively** — "Umowy" and "umowy" in the
  same parent are duplicates. A create or rename that would duplicate a sibling name MUST be
  rejected with a stable "duplicate folder name" error code, without persisting anything.
- **FR-005**: The uniqueness guarantee MUST hold under concurrency — two identical creates in the
  same parent MUST admit at most one — enforced by a database uniqueness constraint (including a
  correct rule for root folders where there is no parent), with the losing operation mapped back to
  the duplicate-name code rather than surfacing an infrastructure error.
- **FR-006**: Users MUST be able to rename a folder they own; a rename MUST change only the name and
  MUST NOT move the folder or alter the position, path, or contents of the folder or its
  descendants. The uniqueness (FR-004) and name-validation (FR-007) rules apply to the new name; a
  rename to the folder's current name is a successful no-op.
- **FR-007**: The system MUST validate folder names on create and rename. The name MUST first be
  normalized by **trimming leading and trailing whitespace**; the trimmed value is what is stored,
  length-checked, and uniqueness-checked. A name that is empty after trimming (including a
  whitespace-only name), exceeds the configured maximum length (default 100 characters), or
  contains the path separator `/` MUST be rejected with a stable "invalid folder name" error code.
  The length limit MUST be configuration-driven with no magic numbers.
- **FR-008**: Users MUST be able to delete a folder they own **only when it is empty** — it contains
  no subfolders and no files. Deleting a non-empty folder MUST be rejected with a stable "folder not
  empty" error code without removing anything, and MUST surface guidance to delete or move the
  contents first. Cascade deletion is out of scope.
- **FR-009**: The emptiness check for deletion MUST account for both subfolders and files. The file
  arm is wired through a forward-looking seam because documents are delivered by US-04; the subfolder
  arm is enforced now, and the check MUST be evaluated so a folder is never deleted while non-empty
  under a concurrent last-child removal.
- **FR-010**: All folder operations MUST be scoped to the current session; accessing, renaming, or
  deleting a folder owned by another session MUST behave as not-found (404), never disclosing its
  existence (isolation inherited from US-01, not re-implemented).
- **FR-011**: All folder failures MUST be returned through the standard `Result` → ProblemDetails
  channel with a stable, machine-readable `code` drawn from the module's error catalog, never as a
  naked 500 (constitution §II).
- **FR-012**: The frontend MUST offer folder actions in the tree — create (new folder), rename, and
  delete — as context actions on tree nodes, MUST NOT offer "New folder" on a folder already at the
  maximum depth, MUST confirm deletion before performing it, and MUST surface each error code as a
  human-readable message (via the shared UI, never native dialogs).
- **FR-013**: Sibling folders MUST be presented in **case-insensitive alphabetical order** by name
  (locale-aware), independent of the order in which they were created, so the tree is stable and
  predictable across sessions and reloads.

### Key Entities *(include if feature involves data)*

- **Folder**: A session-owned node in the document tree. It carries its owning session, a display
  name, a reference to its parent (absent for root folders), and a materialized path whose segments
  are folder identifiers and which encodes its depth and subtree membership. A folder's identity is
  stable across a rename (segments are IDs, not names), which is what keeps rename cheap and
  descendants undisturbed.
- **Folder Limits (configuration)**: The tunable rules — maximum nesting depth (default 3) and
  maximum name length (default 100 characters) — bound from configuration with no magic numbers.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A visitor can create a root folder and a nested folder and see the correct parent/child
  hierarchy in the returned tree, in 100% of create sequences.
- **SC-002**: 100% of attempts to nest a folder beyond the configured depth are rejected before
  anything is persisted, each with the max-depth error code.
- **SC-003**: 100% of attempts to create or rename to a name already used within the same parent are
  rejected with the duplicate-name code, while the same name under a different parent succeeds; under
  two concurrent identical creates the folder count for that name/parent never exceeds one (0%
  over-admit across repeated runs).
- **SC-004**: A rename changes the name in 100% of cases while leaving the folder's position and every
  descendant's location unchanged (0 descendants moved or re-pathed).
- **SC-005**: 100% of attempts to delete a non-empty folder are rejected with the folder-not-empty
  code and remove nothing; empty folders are deleted after confirmation in 100% of cases.
- **SC-006**: 100% of names that are empty after trimming, over the configured length, or contain
  `/` are rejected with the invalid-name code on both create and rename; names differing only by
  case or by surrounding whitespace from an existing sibling are treated as duplicates.
- **SC-008**: Sibling folders are returned in case-insensitive alphabetical order in 100% of tree
  reads, regardless of creation order.
- **SC-007**: One session's folder operations are unaffected by, and invisible to, any other session
  in 100% of cross-session checks; a cross-session folder reference behaves as not-found.

## Assumptions

- **US-01 is in place**: session identity, the `UserSessionId` column, the global query filter, and
  central session stamping already exist and are reused unchanged; folder isolation inherits from
  them rather than being re-implemented.
- **Documents (US-04) are not built here**: the "contains files" arm of the emptiness rule (FR-008/
  FR-009) is wired through a forward-looking seam and validated end-to-end once US-04 lands; US-09
  fully enforces and tests the subfolder arm today.
- **Move/reparent is out of scope**: changing a folder's parent (drag & drop of folders, moving a
  subtree) is delivered by US-10/US-11; US-09 covers create, rename (name only), and delete.
- **Cascade delete is out of scope**: only empty folders may be deleted; deleting a folder together
  with its contents (with confirmation) is explicit future work.
- **Cosmetic attributes are out of scope**: folder colours, icons, and favourites are not part of
  this story.
- **Materialized path is settled**: the hierarchy representation, prefix-based subtree scoping, and
  the depth-by-segment-count rule are binding decisions from the README/constitution, reflected in
  planning rather than re-opened.
- **Limits are configuration-driven**: maximum depth (3) and maximum name length (100) are bound from
  configuration; the path separator reserved from names is `/`.
- **Name comparison is case-insensitive and whitespace-trimmed**: names are trimmed before storage
  and validation, and uniqueness within a parent is evaluated case-insensitively; siblings are
  ordered case-insensitively alphabetically.
