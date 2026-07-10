# Phase 0 Research — Folder CRUD (US-09)

All items below were either fixed by the spec / constitution (recorded for traceability) or are
implementation choices resolved here. No open `NEEDS CLARIFICATION` remain.

## D1 — Materialized-path format

- **Decision**: `path` is a `text` column of the form `/{id}/{id}/…/` — **self-inclusive** (the
  folder's own id is the last segment), with a **leading and trailing slash**. Segments are the
  folders' GUIDs rendered as canonical lowercase `N`-format (`00000000000000000000000000000000`).
  A root folder with id `A` has `path = /A/`; its child `B` has `path = /A/B/`.
- **Rationale**: Leading+trailing slashes make prefix queries exact and unambiguous — a subtree is
  `path LIKE parent.path || '%'` and a segment can never be a false prefix of another (id lengths are
  fixed, and slashes delimit). Self-inclusive paths make `Depth = segment count` and let the folder's
  own row answer "is X in the subtree of Y" without a self-join. IDs (not names) as segments are what
  make rename O(1) (D5).
- **Alternatives considered**: adjacency list + recursive CTE (rejected by the README decision — no
  recursive CTEs); `ltree` extension (extra dependency, GUID segments need escaping); names-in-path
  (breaks rename-O(1) and uniqueness). 

## D2 — Depth limit (max 3 levels)

- **Decision**: Depth = number of path segments. Root = depth 1, its child = depth 2, grandchild =
  depth 3. Creating a child of a depth-3 folder yields depth 4 → rejected with
  `folder.max_depth_exceeded`. Enforced in `Folder.CreateChild(parent, …, maxDepth)` (pure domain,
  reads `FolderOptions.MaxDepth`), so AC-2 is a cheap domain test; the API also hides "New folder" at
  max depth (FR-012).
- **Rationale**: Segment count is derivable from the parent alone (no extra query). Domain-side guard
  keeps the rule testable without a database.
- **Alternatives**: a stored `depth` int column (redundant with path; risk of drift).

## D3 — Case-insensitive uniqueness within a parent (+ concurrent race)

- **Decision**: Two **partial unique indexes** on `LOWER(name)`:
  - root: `UNIQUE (user_session_id, LOWER(name)) WHERE parent_id IS NULL`
  - nested: `UNIQUE (user_session_id, parent_id, LOWER(name)) WHERE parent_id IS NOT NULL`
  The database is the **authority**; the create/rename handlers may do a best-effort pre-check for a
  friendly path, but the AC-3 concurrent-duplicate race is resolved by the constraint. A `23505`
  unique violation is translated to `folder.duplicate_name` by `FoldersExceptionHandler` (reusing
  `NpgsqlPersistenceExceptionClassifier` / `PersistenceErrorKind.UniqueViolation`).
- **Rationale**: Postgres treats `NULL` parent_ids as **distinct**, so a single composite unique
  constraint would leave **root** duplicates unguarded. Two partial indexes enforce both scopes.
  `LOWER(name)` gives case-insensitive matching without the `citext` extension.
- **Alternatives**: `citext` column (extra extension, sorts/compares implicitly — less explicit than
  `LOWER`); `UNIQUE … NULLS NOT DISTINCT` (PG15+, works but the two partial indexes are clearer and
  match the story's "partial unique index for root" wording).

## D4 — Name normalization & validation

- **Decision**: On create and rename, the name is **trimmed** (leading/trailing whitespace) first; the
  trimmed value is what is stored, length-checked, and uniqueness-checked. Invalid when: empty after
  trim (incl. whitespace-only), length > `FolderOptions.MaxNameLength` (default 100), or contains `/`.
  All three → `folder.invalid_name`. Lives in the `Folder` factories (pure), so AC-6 is domain-tested.
- **Rationale**: Trimming before uniqueness prevents "` Umowy `" vs "`Umowy`" duplicates and invisible
  names; reserving `/` protects the path delimiter. Length is config-driven (§VII, no magic numbers).
- **Alternatives**: store raw + trim on read (inconsistent uniqueness); allow `/` with escaping
  (needless complexity for a display name).

## D5 — Rename is O(1)

- **Decision**: Rename updates **only** `name` (after D4 normalization + D3 uniqueness); `path`,
  `parent_id`, and every descendant row are untouched.
- **Rationale**: Because path segments are IDs (D1), a name change never changes any path, so no
  descendant re-pathing is needed — AC-4 holds by construction. Contrast with name-based paths, which
  would require rewriting every descendant on rename.

## D6 — Delete emptiness

- **Decision**: A folder is deletable only when it has **no direct children** and **no files**. The
  subfolder arm is `IFolderRepository.HasChildrenAsync(id)` = `EXISTS(folders WHERE parent_id = id)`
  (a grandchild implies a child, so direct-children suffices). The file arm is the forward-looking
  `IFolderFileProbe.HasFilesAsync(id)` seam, whose US-09 implementation (`NoFolderFilesProbe`) returns
  `false` because `documents.folder_id` does not exist yet; **US-04 replaces it** with a real query.
  The emptiness check + delete run in one transaction so a concurrent last-child removal cannot leave a
  folder wrongly deleted while non-empty. Non-empty → `folder.not_empty`.
- **Rationale**: Keeps AC-5 fully specified and testable today (subfolder arm live, file arm seam-tested
  via a fake probe) while giving US-04 a clean drop-in point. Cascade delete is explicitly out of scope.
- **Alternatives**: cascade delete (out of scope, risks silent subtree loss); checking the whole subtree
  via `path` prefix (unnecessary — direct children are sufficient and cheaper).

## D7 — Minimal folder-list read in US-09 (scope)

- **Decision**: US-09 ships `GET /api/folders` returning the session's folders as a flat, ordered list
  (`id`, `parentId`, `name`, `depth`), so AC-1 ("tree shows the hierarchy") and FR-013 (ordering) are
  **independently testable now**. The client composes the tree from `parentId`. US-07 later adds the
  documents-in-tree view on top of this same read; US-09 does **not** list documents.
- **Rationale**: Mirrors US-05 shipping `GET /api/quota` so its own ACs were verifiable without US-04.
  A CRUD story with no way to observe the result is not independently testable.
- **Alternatives**: defer all reads to US-07 (would strand AC-1/FR-013 verification); return a nested
  tree DTO now (more shape than US-09 needs; US-07 owns the presentation tree).

## D8 — Sibling ordering (FR-013)

- **Decision**: `ListFolders` orders by `LOWER(name)` (case-insensitive), independent of creation
  order. Ordering is applied in the read query; the client renders in received order.
- **Rationale**: Predictable, stable, testable; matches the clarify decision. Collation is the database
  default (locale-aware enough for the case study); a stricter ICU collation can be layered later
  without a contract change.

## D9 — `FolderOptions` binding & path index

- **Decision**: `FolderOptions { int MaxDepth = 3; int MaxNameLength = 100; }` bound from the
  `Folders:*` configuration section via `IOptions<T>`; injected into the create/rename handlers (and
  surfaced to the domain factories as parameters). A `path` index with `text_pattern_ops` supports the
  `LIKE prefix || '%'` subtree queries (delete subtree check today, US-13 scope later).
- **Rationale**: Config-driven limits (§VII); `text_pattern_ops` is required for `LIKE`-prefix index
  usage under non-C collations.
- **Alternatives**: constants in code (violates §VII); no path index (subtree scans as usage grows).
