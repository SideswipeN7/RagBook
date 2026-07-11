# Phase 1 Data Model — Folder & Document Tree (US-07)

US-07 is a read-projection story: no new aggregate, one additive nullable column, and read DTOs. The
tree is composed on the client from two flat, ordered lists.

## Schema change: `Document.FailureReason`

| Property | Type | Notes |
|---|---|---|
| `FailureReason` | `string?` | Human-readable reason a document's processing **failed**. Nullable; **US-07 displays**, **US-06 populates**. Column `failure_reason text NULL` (migration `AddDocumentFailureReason`). No write path in US-07. |

Mapped in `DocumentConfiguration` (`HasColumnName("failure_reason")`, nullable). Existing rows and every
non-failed document keep it null.

## Read seam: `ITreeReader` (`Modules/Tree/Domain/ITreeReader.cs`)

```
Task<TreeData> GetAsync(CancellationToken ct);   // TreeData(IReadOnlyList<TreeFolder> Folders, IReadOnlyList<TreeDocument> Documents)
```

Infrastructure `TreeReader` (over `RagBookDbContext`, session-scoped by the global filter):

- Folders: `dbContext.Folders.AsNoTracking().OrderBy(f => f.Name.ToLower())` → `TreeFolder` (Id, ParentId,
  Name, Depth-from-`FolderPath`).
- Documents: `dbContext.Documents.AsNoTracking().Where(Origin != Demo).OrderByDescending(d => d.UploadedAt)`
  → `TreeDocument`. (Demo excluded for now; US-03 renders the demo section separately.)

Two queries, independent of folder count → no N+1 (FR-001/SC-001).

## Read DTOs (`Modules/Tree/Features/GetTree/`)

- **`TreeResponse`**: `{ IReadOnlyList<TreeFolder> Folders; IReadOnlyList<TreeDocument> Documents; }` — the
  single response of `GET /api/tree`.
- **`TreeFolder`**: `(Guid Id, Guid? ParentId, string Name, int Depth)` — Tree-owned (not the Folders
  module's `FolderNode`, to avoid a cross-module Core reference).
- **`TreeDocument`**: `(Guid Id, Guid? FolderId, string FileName, string ContentType, long SizeBytes,
  string Status, int ChunkCount, DateTimeOffset UploadedAt, string? FailureReason)`.

`Status` is the enum name (`Processing`/`Ready`/`Failed`) as a string, consistent with US-04's
`DocumentResponse.Status`.

## Query + handler

- **`GetTreeQuery : IQuery<TreeResponse>`** — no parameters (session is ambient).
- **`GetTreeQueryHandler`** — calls `ITreeReader.GetAsync`, maps `TreeData` → `TreeResponse` (pass-through
  of the already-ordered lists). Pure read; returns the response directly (no `Result` wrapper needed —
  matches `ListResourcesQueryHandler`/`ListFoldersQueryHandler`).

## Ordering rules → requirement trace

| Rule | Enforced where | Requirement |
|---|---|---|
| Folders alphabetical (case-insensitive) | `TreeReader` `OrderBy(LOWER(name))` | FR-008 |
| Documents newest-first | `TreeReader` `OrderByDescending(UploadedAt)` | FR-008 |
| One request, no N+1 | two fixed queries in `TreeReader` | FR-001 / SC-001 |
| Session isolation | global query filter | FR-012 / SC-007 |
| Demo excluded from session tree | `Where(Origin != Demo)` | FR-013 |

## Frontend view model (`core/tree.store.ts`)

- **`TreeNode`** (client): a folder node `{ kind:'folder', id, name, depth, children: TreeNode[], documents: DocumentRow[] }`
  or a document leaf `{ kind:'document', ...TreeDocument }`. Composed from the flat lists (research D3).
- **`DocumentRow`**: the `TreeDocument` fields plus a derived `displaySize` (via `formatFileSize`) and a
  `displayFailureReason` (the reason or a generic fallback).
- **Expansion**: `expanded: Set<string>` (folder ids) persisted to `sessionStorage` (research D4).

## Validation / display rules

| Rule | Where | Requirement |
|---|---|---|
| Human-readable decimal size (B/KB/MB, 1dp) | `core/file-size.ts` | FR-005 |
| Processing → spinner; Failed → error + reason tooltip; Ready → chunk count | row component | FR-006 |
| Empty state (CTA + demo pointer) when no folders/documents | tree component | FR-007 |
| Long name → ellipsis + full-name `title` | row/folder templates | FR-010 |
| Empty folder → expandable, "empty folder" note | folder node template | FR-011 |
| Expansion persists for the browser session | `TreeStore` + `sessionStorage` | FR-004 / SC-005 |
| Refresh without reload after upload/delete | `TreeStore.refresh()` | FR-009 / SC-006 |
