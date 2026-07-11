# Implementation Plan: Folder & Document Tree (US-07)

**Branch**: `005-us07-tree` | **Date**: 2026-07-11 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/005-us07-tree/spec.md`

## Summary

US-07 adds the read/compose surface and the unified view: a new **`GET /api/tree`** returns the
session's folders and documents in **one** response (two session-scoped queries, no per-folder fan-out),
and the frontend renders them as a single **`@angular/cdk` `cdk-tree`** — folders nested by hierarchy
with their documents inside, root documents at the top level. Each document row shows name, a
human-readable **decimal** size (B/KB/MB, 1 dp), a status badge (processing → spinner, failed → error +
reason tooltip, ready → chunk count), chunk count, and upload date. Folders expand/collapse with the
open/closed state kept in **sessionStorage** (UI state, not server data); an empty session shows an
upload call-to-action + demo pointer; a completed upload/delete refreshes the shared store without a
reload. A **nullable `FailureReason`** column is added to the document now (forward-looking — US-07
displays, US-06 fills). Depends on US-04 + US-09 (shipped).

Technical approach: a small **`Tree`** read slice owns `GetTreeQuery` and a single **`ITreeReader`**
seam (implemented in Infrastructure over the shared `DbContext`), so the Tree core references neither the
Folders nor the Documents module directly (constitution §I) while still composing both entity types in
two `AsNoTracking` queries (folders ordered by `LOWER(name)`, documents by `uploaded_at` desc — FR-008).
The unified `cdk-tree` component **supersedes** the US-09 folders-only `app-folder-tree`, re-providing its
create/rename/delete folder actions and adding document rows; the US-04 upload component stays. Session
isolation is inherited from the global query filter (FR-012). The tree read follows the same DTO
conventions; there are no new domain rules — this is a projection + presentation story.

## Technical Context

**Language/Version**: C# (LangVersion `preview`) on **.NET 10**; TypeScript ~5.8 (Angular 20)

**Primary Dependencies**: ASP.NET Core, **Wolverine** (query dispatch), EF Core + Npgsql, .NET Aspire;
Angular 20 standalone/signals + **`@angular/cdk` `cdk-tree`** (new frontend dependency, matching Angular
20 — per clarification). No new backend package.

**Storage**: PostgreSQL — reuses `folders` (US-09) and `documents` (US-04); adds one nullable column
`documents.failure_reason text NULL` (migration `AddDocumentFailureReason`). No new table; the tree is a
read model.

**Testing**: xUnit + FluentAssertions (Application: `GetTreeQueryHandler` with a mocked `ITreeReader`;
the size formatter is a frontend util tested in the Web project); **Testcontainers** PostgreSQL for the
`GET /api/tree` integration (composition, ordering, isolation, no-N+1). Angular unit tests (TreeStore,
tree component render + statuses + empty state + expansion persistence + size format).

**Target Platform**: Linux container → GCP Cloud Run (stateless API); modern browsers for the SPA.

**Project Type**: Web application (Angular SPA + ASP.NET Core modular monolith), Aspire-orchestrated.

**Performance Goals**: One request loads the whole view; the reader runs exactly **two** queries
regardless of folder count (no N+1) — the SC-001 target. At the modelled max (10 docs, 3 folder levels)
this is trivially fast; no virtualization (out of scope).

**Constraints**: One composed response (FR-001); fixed ordering (FR-008); session isolation → cross-session
invisible (FR-012); expansion state is UI-only (FR-004); errors via `Result`/ProblemDetails; the Tree core
must not reference the Folders/Documents modules directly (one `ITreeReader` seam); migrations out-of-band.

**Scale/Scope**: Case-study scale. US-07 delivers the read endpoint, the `FailureReason` column, and the
unified `cdk-tree` view (folders+documents, statuses, empty state, expansion persistence, size format).
Explicitly **not**: search, configurable sort, virtualization, background processing (US-06), and the
mutating actions themselves (upload = US-04 exists; delete = US-08; move = US-10/11) — US-07 only renders
and exposes the shared refresh.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | US-07 compliance |
|---|---|
| **I. Vertical-slice modular monolith** | New `Tree` read slice (`Modules/Tree/Features/GetTree/`) depends on **its own** `ITreeReader` seam (impl in Infrastructure), so it references neither Folders nor Documents core types directly. ✅ |
| **II. CQRS + Result contract** | `GetTreeQuery : IQuery<TreeResponse>`; a pure read (no failure codes beyond the framework). No new error catalog. `Permissions/` deferred — see Complexity Tracking. ✅ (justified deviation) |
| **III. Data isolation by session** | The reader queries `folders`/`documents` through the global query filter — only the current session's rows compose the tree; cross-session data is invisible (FR-012). ✅ |
| **IV. Test-first** | Application (`GetTreeQueryHandler` composes/orders from a mocked reader), Integration (Testcontainers: composition, ordering FR-008, isolation, single-request/no-N+1), Angular (render, statuses, empty, expansion persistence, size format). ✅ |
| **V. Provider resilience + cache** | No external provider in US-07. ✅ (n/a) |
| **VI. Auditing & time** | No writes; `UploadedAt`/audit already stamped (US-04). Dates are formatted for display only. ✅ |
| **VII. Secrets** | None. No new config limits (ordering/size are fixed conventions). ✅ |
| **VIII. Operations & delivery** | One additive migration (`AddDocumentFailureReason`, nullable) applied out-of-band; `@angular/cdk` pinned to the Angular 20 line. ✅ |
| **IX. Frontend & design system** | `cdk-tree` component is standalone, OnPush, signals; design tokens (no inline hex); status badges/spinner/error use shared styles; long names truncate with a title tooltip; empty state with CTA. Re-provides US-09 folder actions. ✅ |

**Gate result: PASS** with one justified deviation (`Permissions/` still deferred — anonymous sessions).

## Project Structure

### Documentation (this feature)

```text
specs/005-us07-tree/
├── plan.md              # This file
├── research.md          # Phase 0 — decisions (one reader seam, two-query compose, cdk-tree, size format, failure_reason, expansion persistence)
├── data-model.md        # Phase 1 — Tree DTOs, FailureReason column, ITreeReader, ordering rules
├── quickstart.md        # Phase 1 — run & validate AC-1..AC-5
├── contracts/
│   └── tree-api.md       # Phase 1 — GET /api/tree response shape + reader seam
├── checklists/
│   └── requirements.md  # spec quality checklist
└── tasks.md             # Phase 2 — /speckit-tasks output
```

### Source Code (repository root) — new/changed for US-07

```text
src/
├── RagBook/                                          # Core
│   ├── Modules/Documents/Domain/Document.cs           # + nullable FailureReason (forward-looking; US-06 fills)
│   └── Modules/Tree/                                   # new read slice
│       ├── Domain/ITreeReader.cs                       # GetAsync(ct) → folders + documents — one seam, no cross-module Core refs
│       └── Features/GetTree/
│           ├── GetTreeQuery.cs                          # IQuery<TreeResponse>
│           ├── GetTreeQueryHandler.cs                   # composes + orders (folders A→Z, documents by date desc)
│           ├── TreeResponse.cs                          # { Folders: TreeFolder[], Documents: TreeDocument[] }
│           ├── TreeFolder.cs                            # Id, ParentId, Name, Depth
│           └── TreeDocument.cs                          # Id, FolderId, FileName, ContentType, SizeBytes, Status, ChunkCount, UploadedAt, FailureReason
├── RagBook.API/
│   ├── Program.cs                                     # MapTreeEndpoints()
│   └── Endpoints/TreeEndpoints.cs                     # GET /api/tree
├── RagBook.Infrastructure/
│   ├── DependencyInjection.cs                         # + ITreeReader → TreeReader
│   └── SharedContext/Persistence/
│       ├── Configurations/DocumentConfiguration.cs    # + failure_reason column
│       └── TreeReader.cs                               # two AsNoTracking queries (folders, documents), session-scoped + ordered
├── RagBook.Infrastructure.Migrations/Migrations/      # AddDocumentFailureReason (nullable column)
└── Web/
    ├── package.json                                   # + @angular/cdk ^20
    └── src/app/
        ├── core/tree.store.ts                        # signals: fetch GET /api/tree, compose nested tree, expansion set (sessionStorage), refresh()
        ├── core/file-size.ts                         # decimal B/KB/MB (1 dp) formatter (+ unit test)
        └── documents/tree/                            # unified cdk-tree component (folders + document rows), row component, empty state
tests/
├── RagBook.Application.Tests/Tree/                     # GetTreeQueryHandlerTests (mocked ITreeReader: compose + ordering)
└── RagBook.Api.IntegrationTests/Tree/                 # TreeEndpointTests (compose, ordering, isolation, single-request)
```

**Note on superseding US-09 UI**: the new tree component replaces `app-folder-tree` in the shell and
re-provides its create/rename/delete folder actions (reusing `FolderTreeStore` methods) plus document
rows. `FolderTreeStore` is kept for the folder mutations it already owns; `TreeStore` owns the composed
read + expansion state. The US-04 `app-document-upload` component is unchanged.

**Structure Decision**: US-07 is a read-projection + presentation slice. The one `ITreeReader` seam keeps
the Tree core free of cross-module references while composing folders + documents in two queries; the only
schema change is the additive nullable `FailureReason` column; the frontend gains `@angular/cdk` and a
unified tree that supersedes the folders-only one.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| New `@angular/cdk` frontend dependency | Product clarification chose `cdk-tree` for the unified tree (built-in tree control + keyboard/ARIA). | Extending the recursive component avoids the dep but was explicitly not chosen; re-litigating is out of scope. |
| A **new `ITreeReader` seam** rather than reusing `IFolderRepository` + a documents read | Composing two entity types in one read without the Tree core referencing either module keeps §I intact; one seam = one Infrastructure impl doing two queries (no N+1). | Injecting both modules' repositories into the Tree handler would couple three modules in Core; a per-folder document query would be N+1. |
| Forward-looking nullable `FailureReason` column (populated only by US-06) | AC-2 needs a failure reason to display; adding the column now avoids a second document migration in US-06 and keeps the read model stable. | Synthesizing the reason purely in the read model was the rejected clarification option; it defers a schema change and a re-migration to US-06. |
| `Tree` module still ships **no `Permissions/`** (§II) | Anonymous sessions; the read is uniformly scoped to the owning session by the query filter. | Empty scaffolding; re-introduced by the first story with a real permission surface. |

## Phase notes

- **Phase 0 (research.md)** — decisions: the single `ITreeReader` seam + two-query compose (no N+1);
  `cdk-tree` nested-tree wiring and how folder actions are re-provided; the decimal size formatter;
  the nullable `FailureReason` column; expansion-state persistence in `sessionStorage` (keyed, UI-only).
- **Phase 1 (data-model.md, contracts/, quickstart.md)** — the Tree DTOs + `ITreeReader`; the
  `documents.failure_reason` column; the `GET /api/tree` contract; the runnable quickstart proving
  AC-1..AC-5.
- **Phase 2 (tasks.md)** — produced by `/speckit-tasks`, Red→Green→Refactor per tier; the integration
  composition/ordering/isolation tests and the Angular render/expansion/size tests land with their code.
