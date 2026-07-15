# Implementation Plan: Przenoszenie plików — drag & drop (US-10)

**Branch**: `015-us10-drag-drop` | **Date**: 2026-07-15 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/015-us10-drag-drop/spec.md`

## Summary

Move a document to a folder (or the root) by drag-and-drop, with an **optimistic** UI and **rollback** on failure,
plus a context-menu "Przenieś do…" fallback. The backend is a trivial folder change: a `Documents/Move` slice +
`PATCH /api/documents/{id}/folder` that validates ownership + the target folder + a read-only (demo) guard, no-ops
when the folder is unchanged, and changes only `documents.folder_id` (chunks/vectors untouched). No migration —
the column exists. The value lives in the frontend: `@angular/cdk/drag-drop`, an optimistic `moveDocument` in
`TreeStore` that reverts on error and surfaces a design-system notice, drop-target highlighting, a root drop-zone,
and the parity menu action.

## Technical Context

**Language/Version**: C# (.NET 10) backend; TypeScript / Angular 20 frontend.

**Primary Dependencies**: Wolverine (CQRS dispatch); existing `Document` aggregate + `IFolderReference`
(session-scoped folder existence, Documents-owned seam); `@angular/cdk` drag-drop (the tree already uses
`@angular/cdk` `cdk-tree`); `TreeStore` (US-07); the signal-notice pattern (`NotFoundNotifier` → `shell__notice`).

**Storage**: PostgreSQL — a single `UPDATE documents SET folder_id = …`. **No migration, no new entity.**

**Testing**: xUnit + NSubstitute + FluentAssertions (Domain/Application/Integration); Testcontainers for the move
+ isolation + chunks-untouched; Karma for the optimistic/rollback store logic + drop/menu component behaviour.

**Target Platform**: Linux container backend; evergreen browser SPA.

**Project Type**: Web (modular-monolith .NET backend + Angular SPA).

**Performance Goals**: The move is one indexed single-row update; the UI reflects it before the round-trip
(optimistic).

**Constraints**: CQRS + `Result` → ProblemDetails; session isolation (cross-session → 404); design tokens, no
native dialogs, ≥360px; drag-and-drop MUST have a menu equivalent (accessibility); no vector-index change.

**Scale/Scope**: Small backend slice + a focused frontend interaction. Single-document moves only (bulk = US-12).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **I. Vertical-Slice Modular Monolith** ✅ — `Documents/Features/MoveDocument` (command + handler); endpoint in
  `RagBook.API`; a narrow `IDocumentMoveRepository` in Infrastructure. Folder existence goes through the existing
  `IFolderReference` seam — no direct Folders-module reference.
- **II. CQRS + Result Contract** ✅ — `MoveDocumentCommand` → `Result` → ProblemDetails; `DocumentErrors` gains
  `document.read_only`; reuses `document.not_found` + `folder.not_found`.
- **III. Data Isolation** ✅ — the document + the target folder are read through the session query filter; a
  cross-session document/folder reads as absent → 404. A Testcontainers test proves cross-session refusal.
- **IV. Test-First** ✅ — Domain (`Document.MoveToFolder`), Application (handler branches: not-found / read-only /
  folder-not-found / no-op / move), Integration (move to folder + root, guards, **chunks untouched**, isolation),
  Angular (optimistic move + rollback in `TreeStore`; drop-target highlight; menu parity). Red→Green.
- **V. Providers** ✅ — no external call; nothing config/magic-number related added.
- **VI/VII/VIII** ✅ — no time/secret surface; **no migration** (only an `UPDATE`); no startup work.
- **IX. Frontend & Design System** ✅ — `@angular/cdk` drag-drop, standalone/OnPush/signals, design tokens, the
  rollback surfaced via the shared signal-notice (never `window.confirm`/`alert`), works at ≥360px, and the
  "Przenieś do…" menu gives drag-and-drop a required non-pointer equivalent.

**Result: PASS** — no violations; Complexity Tracking empty.

## Project Structure

### Documentation (this feature)

```text
specs/015-us10-drag-drop/
├── plan.md, research.md, data-model.md, quickstart.md
├── contracts/move-document.md
├── checklists/requirements.md
└── tasks.md   (/speckit-tasks)
```

### Source Code (repository root)

```text
src/RagBook/Modules/Documents/
├── Domain/Document.cs                 # + MoveToFolder(Guid? folderId)
├── Domain/IDocumentMoveRepository.cs  # GetByIdAsync (session-filtered, tracked) + SaveChangesAsync
├── Errors/DocumentErrors.cs           # + ReadOnly (document.read_only)
└── Features/MoveDocument/{MoveDocumentCommand,MoveDocumentCommandHandler}.cs

src/RagBook.Infrastructure/SharedContext/Persistence/DocumentMoveRepository.cs   # + DI registration
src/RagBook.API/Endpoints/DocumentEndpoints.cs   # + PATCH /api/documents/{id}/folder (+ MoveDocumentRequest)

src/Web/src/app/
├── core/tree.store.ts                 # + moveDocument(documentId, targetFolderId|null): optimistic + rollback + PATCH
└── documents/tree/
    ├── document-tree.*                # cdkDropListGroup; folder nodes + root zone as drop targets; highlight; error notice
    └── document-row.*                 # cdkDrag; context menu "Przenieś do…" (folders + root) → moveDocument

tests/
├── RagBook.Domain.Tests/Documents/DocumentMoveTests.cs         # MoveToFolder
├── RagBook.Application.Tests/Documents/MoveDocumentHandlerTests.cs  # branches
├── RagBook.Api.IntegrationTests/Documents/MoveDocumentEndpointTests.cs  # move/root/guards/chunks/isolation
└── src/Web (Karma)                    # tree.store move+rollback; document-tree drop; document-row menu
```

**Structure Decision**: A standard Documents vertical slice for the move (mirrors `DeleteDocument`), with folder
existence via the existing `IFolderReference`. The optimistic state + rollback live in `TreeStore` (it owns the
tree), keeping the drag-drop component thin. No migration.

## Complexity Tracking

*No constitution violations — no entries.*
