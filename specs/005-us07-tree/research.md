# Phase 0 Research — Folder & Document Tree (US-07)

Items fixed by spec/clarify/constitution are recorded for traceability; implementation choices are
resolved here. No open `NEEDS CLARIFICATION` remain.

## D1 — One `ITreeReader` seam, two-query compose (no N+1)

- **Decision**: The `Tree` module defines `ITreeReader.GetAsync(ct)` → a `TreeData(folders, documents)`
  record. The Infrastructure `TreeReader` runs **two** `AsNoTracking` queries on the shared `DbContext`:
  folders `ORDER BY LOWER(name)`, documents `ORDER BY uploaded_at DESC` — both session-scoped by the
  global query filter. `GetTreeQueryHandler` maps them to `TreeResponse` (`TreeFolder[]`, `TreeDocument[]`).
- **Rationale**: Composing two entity types in one read without the Tree core referencing the Folders or
  Documents modules (constitution §I). Two fixed queries mean **no per-folder fan-out** (SC-001/FR-001);
  the client nests folders + places documents by `folderId`. Ordering is done in SQL (FR-008) so the
  client renders as received.
- **Alternatives**: injecting `IFolderRepository` + a documents repo into the handler (couples three
  modules in Core); a per-folder document query (N+1); a database view/JSON aggregate (premature for the
  modelled scale).

## D2 — `cdk-tree` for the unified tree

- **Decision**: Add `@angular/cdk` (`^20`, matching Angular 20) and build the tree with `cdkTree` +
  `NestedTreeControl<TreeNode>`. Nodes are a discriminated union: a **folder node** (children = subfolders
  + its documents) and a **document leaf**. Root documents are top-level leaves. Folder nodes carry the
  US-09 create/rename/delete actions (delegating to `FolderTreeStore`); document leaves render the metadata
  row.
- **Rationale**: The clarification chose `cdk-tree`; it provides the tree control, ARIA roles, and toggle
  machinery. `NestedTreeControl` fits a ≤3-level nested model without virtualization.
- **Alternatives**: extending the existing recursive `ng-template` tree (rejected in clarification);
  `FlatTreeControl` (needs flattening + level bookkeeping — nested is simpler for this shape).

## D3 — Nested node model built on the client

- **Decision**: `TreeStore` (signals) fetches `GET /api/tree`, then composes `TreeNode[]`: index folders
  by id, attach child folders by `parentId`, attach documents to their `folderId` bucket (or the root
  bucket for `folderId == null`). Within a folder, child **folders render before documents** (folders
  A→Z, documents newest-first — the server already ordered each list). The store exposes `roots` (computed)
  and a `refresh()` the mutation stores call (FR-009).
- **Rationale**: The server returns two flat, ordered lists; the client owns the cheap composition. Keeping
  it in a signals store makes refresh reactive and shareable with upload (US-04) / delete (US-08).
- **Alternatives**: server-built nested JSON (couples the API to the render shape); building in the
  component (not reusable/refreshable).

## D4 — Expansion state in `sessionStorage` (UI-only)

- **Decision**: The set of expanded folder ids is a signal persisted to `sessionStorage` under a fixed key
  (e.g. `ragbook.tree.expanded`), read on init and written on every toggle. It is **UI state**, never sent
  to the server (FR-004).
- **Rationale**: Survives in-app navigation within the browser session (SC-005) without a backend round
  trip or new persisted data. `sessionStorage` (not `localStorage`) scopes it to the tab/session, matching
  "for the duration of the browser session".
- **Alternatives**: `localStorage` (persists too long, across sessions); component state (lost on
  navigation); server persistence (out of scope, it is not domain data).

## D5 — Decimal human-readable size formatter

- **Decision**: A pure `formatFileSize(bytes)` util: `< 1,000` → `"{n} B"`; `< 1,000,000` →
  `"{n/1e3, 1dp} KB"`; else `"{n/1e6, 1dp} MB"` (decimal, 1 MB = 1,000,000 bytes — matching the quota).
  One decimal place; trailing `.0` kept for consistency (e.g. `2.0 MB`).
- **Rationale**: The clarified convention, consistent with US-05's decimal MB. A pure function is unit
  tested in isolation (Web project) and reused by every document row.
- **Alternatives**: binary `KiB/MiB` (inconsistent with the app's decimal MB); Angular's built-in — no
  first-class decimal file-size pipe, and a custom util keeps the convention explicit.

## D6 — Nullable `FailureReason` column (forward-looking)

- **Decision**: Add `Document.FailureReason` (`string?`) mapped to `documents.failure_reason text NULL`
  (migration `AddDocumentFailureReason`). US-07 surfaces it in `TreeDocument`; **US-06 populates it** on a
  failed transition. A failed document with a null reason shows a generic "processing failed" message in
  the UI (spec edge case).
- **Rationale**: AC-2 needs a reason to display; adding the column now (nullable, no backfill) avoids a
  second document migration in US-06 and keeps the read model stable. No write path in US-07 (display
  only); tests seed it via EF property metadata (as US-05's seed sets `Status`).
- **Alternatives**: synthesizing the reason in the read model only (rejected in clarification — pushes a
  schema change + re-migration into US-06).

## D7 — Superseding the US-09 folders-only tree

- **Decision**: The new `app-document-tree` (working name) replaces `app-folder-tree` in the shell. Folder
  create/rename/delete actions move onto folder nodes, delegating to the existing `FolderTreeStore`
  methods (unchanged); `TreeStore` owns the composed read + expansion. The old `folder-tree.*` component is
  removed once parity is in place; `FolderTreeStore` is retained for its mutations. The US-04
  `app-document-upload` is unchanged and continues to call `FolderTreeStore.refresh()`/`TreeStore.refresh()`.
- **Rationale**: One tree is the story's core ("jeden widok główny"); keeping two trees would duplicate
  folder rendering. Reusing `FolderTreeStore` mutations avoids rewriting US-09 behavior.
- **Refresh wiring (analyze I1)**: the unified tree reads from `TreeStore` (`GET /api/tree`), so every
  folder mutation (create/rename/delete via `FolderTreeStore`) MUST also trigger **`TreeStore.refresh()`**
  on success — otherwise the tree goes stale after a folder op (AC-4/FR-009). Either the tree component
  calls `TreeStore.refresh()` after the mutation completes, or the folder actions are routed through a
  wrapper that refreshes the tree. `FolderTreeStore.refresh()` alone (which hits `/api/folders`) does not
  update the `TreeStore`-backed view.
- **Alternatives**: keeping `app-folder-tree` alongside a separate document list (two views, contradicts
  the unified-tree decision).
