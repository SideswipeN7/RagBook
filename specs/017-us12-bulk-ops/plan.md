# Implementation Plan: Operacje zbiorcze na plikach — bulk move / delete (US-12)

**Branch**: `017-us12-bulk-ops` | **Date**: 2026-07-16 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/017-us12-bulk-ops/spec.md`

## Summary

Two bulk actions over a de-duplicated document-id list — **move** (to a folder or root) and **delete** — each a
single request with **all-or-nothing** semantics: validate **every** id first, then apply in one transaction; if
any id is invalid, refuse the whole operation with a **`422` ProblemDetails** carrying `code:
document.bulk_validation_failed` + a `failures: [{ id, code }]` extension, and change nothing. Backend adds
`Documents/BulkMove` + `Documents/BulkDelete` slices, an `IDocumentBulkRepository` (transactional multi-move /
multi-delete reusing the US-08 cascade), a `BulkOptions.MaxItems` cap, and the reason codes (reusing
`document.not_found` / `document.read_only` / `folder.not_found`). Frontend adds a selection store (ticked ids +
failed-id highlight), a bulk action bar ("N zaznaczonych: Przenieś do… | Usuń | Anuluj"), a folder picker for
move, a design-system confirm for delete, and clears the selection + refreshes the tree/quota on success. No
migration.

## Technical Context

**Language/Version**: C# (.NET 10) backend; TypeScript / Angular 20 frontend.

**Primary Dependencies**: Wolverine (CQRS); the `Document` aggregate + `IFolderReference` (US-10), the
delete-cascade transaction pattern (US-08), `TreeStore` + `QuotaStore` (US-05/07); design tokens.

**Storage**: PostgreSQL — a bulk move is one `SaveChanges` over N tracked documents' `folder_id`; a bulk delete is
one transaction removing N rows (chunks cascade). **No migration, no new entity.**

**Testing**: xUnit + NSubstitute + FluentAssertions (Application/Integration); Testcontainers for bulk move/delete
+ cascade + quota + all-or-nothing + isolation; Karma for the selection store, action bar, confirm, failure marks.

**Target Platform**: Linux container backend; evergreen browser SPA.

**Project Type**: Web (modular-monolith .NET backend + Angular SPA).

**Performance Goals**: One round-trip per bulk action; the id list is capped (`BulkOptions.MaxItems`, default 50).

**Constraints**: All-or-nothing in one transaction (validate-all-then-apply); session isolation (a foreign id →
not-found, no disclosure); the failure list is `422` ProblemDetails + `failures[]` (empty/over-cap → `400`); design
tokens, no native dialogs, ≥360px. Bulk move doesn't touch the vector index; bulk delete cascades chunks + lowers
quota.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **I. Vertical-Slice Modular Monolith** ✅ — `Documents/Features/{BulkMove,BulkDelete}` slices; endpoints in
  `RagBook.API`; a narrow `IDocumentBulkRepository` (Infrastructure). Folder existence via the existing
  `IFolderReference` seam; delete reuses the US-08 cascade.
- **II. CQRS + Result Contract** ✅ (with a justified shape) — the single-`Error` `Result` can't carry a per-id
  list, so the handlers return a small `BulkResult` (Success / BadRequest(Error) / ValidationFailed(failures)); the
  endpoint still resolves to **one** wire outcome: `204`, a `400` ProblemDetails (empty/over-cap, via
  `ProblemResults`), or a `422` ProblemDetails with `code` + `failures` (a `BulkProblemResults` helper). Failure is
  always code-based ProblemDetails — the contract holds. New codes: `document.bulk_validation_failed`,
  `document.bulk_empty`, `document.bulk_too_large`.
- **III. Data Isolation** ✅ — `GetByIdsAsync` reads through the session filter, so a foreign/unknown id is simply
  absent → reported as not-found; no bulk write touches another session. Testcontainers proves it.
- **IV. Test-First** ✅ — Application (all-ok; one-bad-rejects-all-with-no-change; dedup; empty/over-cap; move
  target-folder-missing), Integration (bulk move; bulk delete cascade + quota−N; all-or-nothing nothing-changed +
  failures list; isolation), Angular (selection toggle/clear/range; action bar; confirm→bulk-delete; picker→bulk-
  move; failure marks). Red→Green.
- **V. Providers** ✅ — no external call; `BulkOptions.MaxItems` is config (no magic number).
- **VI/VII/VIII** ✅ — no time/secret; **no migration**; delete/move each one transaction.
- **IX. Frontend & Design System** ✅ — a selection store + action bar + **design-system delete confirm** (count +
  names, never `window.confirm`), tokens, ≥360px; on success clear selection + refresh tree/quota; on `422` mark
  the offending items.

**Result: PASS** — no violations; Complexity Tracking empty (the `BulkResult` shape is the constitution-consistent
way to carry a list to a code-based ProblemDetails, not a deviation).

## Project Structure

### Documentation (this feature)

```text
specs/017-us12-bulk-ops/
├── plan.md, research.md, data-model.md, quickstart.md
├── contracts/bulk-operations.md
├── checklists/requirements.md
└── tasks.md   (/speckit-tasks)
```

### Source Code (repository root)

```text
src/RagBook/Modules/Documents/
├── BulkOptions.cs                     # MaxItems (50) — SectionName "Bulk"
├── Domain/
│   ├── IDocumentBulkRepository.cs     # GetByIdsAsync (session-filtered) + MoveAllAsync + DeleteAllAsync (transactional)
│   └── BulkFailure.cs / BulkResult.cs # {id, code} + Success/BadRequest/ValidationFailed
├── Errors/DocumentErrors.cs           # + BulkEmpty, BulkTooLarge; const BulkValidationFailed code
└── Features/{BulkMove,BulkDelete}/*   # commands + handlers (validate-all → apply)

src/RagBook.Infrastructure/SharedContext/Persistence/DocumentBulkRepository.cs   # + DI
src/RagBook.API/
├── ProblemDetails/BulkProblemResults.cs   # 422 ProblemDetails + failures[] extension
└── Endpoints/DocumentEndpoints.cs         # + POST /api/documents/bulk-move, /bulk-delete (+ request DTOs)

src/Web/src/app/
├── core/selection.store.ts            # selected ids + failedIds; toggle/clear; bulkMove/bulkDelete (HttpClient) → refresh tree+quota
└── documents/tree/
    ├── document-tree.*                # checkboxes on document leaves; the bulk action bar; delete confirm; move picker
    └── document-row.*                 # a select checkbox (or in the leaf row)

tests/
├── RagBook.Application.Tests/Documents/{BulkMove,BulkDelete}HandlerTests.cs
├── RagBook.Api.IntegrationTests/Documents/BulkOperationsEndpointTests.cs
└── src/Web (Karma)                    # selection.store; action bar; confirm; failure marks
```

**Structure Decision**: Two Documents slices reusing the US-08 delete-cascade + US-10 move validations, with a
narrow `IDocumentBulkRepository` for the transactional apply. The endpoint maps a `BulkResult` to `204` / `400` /
`422+failures`. The frontend adds a `selection.store` (selection + bulk calls) and a bulk action bar in the tree.
No migration.

## Complexity Tracking

*No constitution violations — no entries.*
