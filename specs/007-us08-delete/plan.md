# Implementation Plan: Delete Document (US-08)

**Branch**: `007-us08-delete` (stacked on `fm/us06-processing`) | **Date**: 2026-07-11 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/007-us08-delete/`

## Summary

US-08 adds a **`DELETE /api/documents/{id}`** slice that hard-deletes a document and its whole index. The
handler removes the **database row first** (in a transaction; the US-06 `documents → chunks` FK
`ON DELETE CASCADE` removes the chunks — no application-level chunk deletion), then makes a **best-effort**
removal of the binary via `IFileStorage` (a storage failure is logged, not surfaced — an orphaned blob is
the accepted MVP trade-off). A document owned by another session is invisible, so its id deletes as
**not-found (404)** (`document.not_found`); deleting an already-gone id is likewise 404 (idempotent from
the visitor). Deleting a **processing** document just succeeds — the US-06 worker already aborts quietly
when the record is gone. The frontend adds a **Delete** action with an **inline confirmation** (the same
pattern as folder delete — no native dialog) on document rows in the tree; on success it refreshes the
tree and the quota so the row disappears and the counter drops without a reload. No schema change (the
cascade FK exists from US-06). Depends on US-04/05/06/07 (all on this stacked branch).

## Technical Context

**Language/Version**: C# (LangVersion `preview`) on **.NET 10**; TypeScript ~5.8 (Angular 20)

**Primary Dependencies**: ASP.NET Core, **Wolverine** (command dispatch), EF Core + Npgsql, Angular
standalone/signals. No new package.

**Storage**: PostgreSQL — reuses `documents` (US-04) + `chunks` (US-06). The delete relies on the existing
`chunks.document_id` FK `ON DELETE CASCADE`; **no migration**.

**Testing**: xUnit + FluentAssertions — Application (`DeleteDocumentCommandHandler` with a mocked deletion
repo: deletes vs not-found), **Testcontainers** Integration (cascade removes chunks, cross-session → 404,
delete-during-processing then worker aborts quietly, storage-failure still deletes). Angular unit tests
(delete store issues `DELETE` + refreshes tree/quota; the row's confirm action).

**Target Platform**: Linux container → GCP Cloud Run; modern browsers for the SPA.

**Project Type**: Web application (Angular SPA + ASP.NET Core modular monolith), Aspire-orchestrated.

**Performance Goals**: Not a driver — a single delete (one transactional DELETE + a cascade + one blob
delete).

**Constraints**: DB-first then best-effort blob (orphan tolerated, logged); chunks cascade at the DB (one
source of consistency); session isolation → 404 (never 403); idempotent-from-user; errors via `Result` →
ProblemDetails (`document.not_found`); confirmation in the UI (no `window.confirm`).

**Scale/Scope**: Small. US-08 delivers the delete command/endpoint, the best-effort blob cleanup, and the
row delete action. Explicitly **not**: trash/restore, folder-with-content delete (US-09), bulk delete
(US-12), and the chat/citation UI (US-14/16 — AC-5 is only the clean-404 guarantee).

## Constitution Check

| Principle | US-08 compliance |
|---|---|
| **I. Vertical-slice modular monolith** | New `Documents/Features/DeleteDocument/` slice; reuses `IFileStorage` (US-04) and the DB cascade (US-06). No cross-module call. ✅ |
| **II. CQRS + Result contract** | `DeleteDocumentCommand : ICommand`; the handler returns `Result` (`document.not_found` → 404). `Permissions/` deferred — see Complexity Tracking. ✅ (justified) |
| **III. Data isolation by session** | The deletion repository loads/deletes through the session query filter, so a cross-session id is invisible → not-found (404); no document of another session is ever removed. ✅ |
| **IV. Test-first** | Application (handler: deleted vs not-found, best-effort blob), Integration (cascade, isolation 404, delete-during-processing quiet-abort, storage-failure tolerated). ✅ |
| **V. Provider resilience + cache** | n/a (no external provider). ✅ |
| **VI. Auditing & time** | Hard delete (no audit trail by design — no trash). No `DateTime.UtcNow`. ✅ |
| **VII. Secrets** | None. ✅ |
| **VIII. Operations & delivery** | **No migration** (the cascade FK exists from US-06). ✅ |
| **IX. Frontend & design system** | Delete action + **inline confirm** (reusing the folder-delete pattern, no native dialog); standalone/signals; refreshes `TreeStore`/`QuotaStore` without a reload; design tokens. ✅ |

**Gate result: PASS** with one justified deviation (`Permissions/` still deferred — anonymous sessions).

## Project Structure

```text
src/
├── RagBook/                                          # Core
│   └── Modules/Documents/
│       ├── Domain/IDocumentDeletionRepository.cs      # DeleteAsync(id) → bool (session-scoped; false = not found)
│       ├── Errors/DocumentErrors.cs                   # + document.not_found
│       └── Features/DeleteDocument/
│           ├── DeleteDocumentCommand.cs               # ICommand (Id)
│           └── DeleteDocumentCommandHandler.cs        # DeleteAsync → not-found → Result; success otherwise
├── RagBook.API/
│   └── Endpoints/DocumentEndpoints.cs                 # + DELETE /api/documents/{id}
├── RagBook.Infrastructure/
│   ├── DependencyInjection.cs                         # + IDocumentDeletionRepository → DocumentDeletionRepository
│   └── SharedContext/Persistence/DocumentDeletionRepository.cs  # tx delete (chunks cascade) → commit → best-effort IFileStorage.DeleteAsync + log
└── Web/src/app/
    ├── core/document-actions.store.ts                # delete(id) → DELETE /api/documents/{id} → refresh Tree + Quota
    └── documents/tree/document-tree.*                # document-leaf Delete action + inline confirm (reuse folder pattern)
tests/
├── RagBook.Application.Tests/Documents/               # DeleteDocumentCommandHandlerTests
└── RagBook.Api.IntegrationTests/Documents/            # DeleteDocumentEndpointTests (cascade, 404, during-processing, storage-failure)
```

**Structure Decision**: the delete lives in the Documents module as a thin slice. The DB delete + cascade
+ **best-effort blob cleanup with logging** are encapsulated in the Infrastructure
`DocumentDeletionRepository` (which has both `IFileStorage` and `ILogger`), so the Core handler stays
trivial (`bool → Result`). The frontend reuses the US-07 tree's inline-confirm pattern for document leaves.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| Blob cleanup + logging inside the **Infrastructure repository** rather than the Core handler | Keeps the Core handler free of `IFileStorage` orchestration + `ILogger`, and makes the DB-then-blob ordering atomic in one place. | Doing it in the handler would push logging/storage sequencing into Core and complicate the thin command handler. |
| `Documents` module still ships **no `Permissions/`** (§II) | Anonymous sessions; delete applies to the owning session only (enforced by the query filter). | Empty scaffolding; deferred consistently. |

## Phase notes

- **Phase 0 (research.md)** — decisions: DB-first-then-best-effort-blob ordering + orphan tolerance;
  cascade-at-DB (reuse US-06 FK, no app chunk delete); session-scoped delete → 404 + idempotent-from-user;
  the quiet-worker-abort reuse (US-06) for delete-during-processing; frontend inline-confirm reuse.
- **Phase 1 (data-model.md, contracts/, quickstart.md)** — `IDocumentDeletionRepository` + `DocumentErrors.NotFound`;
  the `DELETE /api/documents/{id}` contract; the runnable quickstart proving AC-1..AC-4 (+ storage-failure).
- **Phase 2 (tasks.md)** — `/speckit-tasks`, Red→Green→Refactor; the cascade / 404 / during-processing /
  storage-failure integration tests land with their code.
