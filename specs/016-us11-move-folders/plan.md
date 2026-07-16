# Implementation Plan: Przenoszenie folderów — z poddrzewem (US-11)

**Branch**: `016-us11-move-folders` | **Date**: 2026-07-15 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/016-us11-move-folders/spec.md`

## Summary

Move a folder with its whole subtree by drag-and-drop (or a "Przenieś do…" menu), optimistically with rollback.
Backend: a `Folders/Move` slice + `PATCH /api/folders/{id}/parent` that, in **one transaction**, validates
ownership / cycle / depth / duplicate-name, then re-parents the folder and rewrites the materialized `path` prefix
of the folder **and every descendant** in a single bulk `UPDATE` (documents untouched). Cycle detection is one
materialized-path comparison (`FolderPath.IsPrefixOf`); the bulk update is explicitly session-scoped (a global
filter does not apply to raw SQL). Frontend: extend the US-10 CDK drag-drop to folder nodes — `onDrop` routes
folder-vs-document by `kind`; `TreeStore.moveFolder` optimistically re-parents (the composed tree re-nests the
subtree by `parentId`) then `PATCH`s, reverting + a notice on failure and refreshing on success to correct
paths/depths; a `cdkDropListEnterPredicate` (and the menu) exclude the folder's own subtree. No migration.

## Technical Context

**Language/Version**: C# (.NET 10) backend; TypeScript / Angular 20 frontend.

**Primary Dependencies**: Wolverine (CQRS); the `Folder` aggregate + `FolderPath` (materialized path, `IsPrefixOf`)
+ `IFolderRepository` + `FolderOptions.MaxDepth` (US-09); `FoldersExceptionHandler` (unique-name → `duplicate_name`);
`@angular/cdk/drag-drop` + `TreeStore` (US-07/US-10).

**Storage**: PostgreSQL — a transaction of two `UPDATE`s (bulk path-prefix rewrite + the moved folder's
`parent_id`), session-filtered in SQL. **No migration, no new entity.**

**Testing**: xUnit + NSubstitute + FluentAssertions (Domain/Application/Integration); Testcontainers for the
subtree-path rewrite + cycle/depth/duplicate + isolation + docs-untouched + chat-scope-follows; Karma for the
optimistic re-nest/rollback, the enter-predicate, `onDrop` routing, and the menu.

**Target Platform**: Linux container backend; evergreen browser SPA.

**Project Type**: Web (modular-monolith .NET backend + Angular SPA).

**Performance Goals**: The move is one transaction with two indexed updates (the bulk one uses the `path` pattern
index); the UI re-nests before the round-trip.

**Constraints**: Atomic (no observable partial move); cycle/depth/name invariants preserved; session isolation
incl. the **raw bulk update explicitly filtering `user_session_id`**; CQRS + `Result` → ProblemDetails; design
tokens, no native dialogs, ≥360px; a menu fallback for a11y parity. No document/vector change.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **I. Vertical-Slice Modular Monolith** ✅ — `Folders/Features/MoveFolder` (command + handler); endpoint in
  `RagBook.API`; a narrow `IFolderMoveRepository` (transactional raw SQL) in Infrastructure. No cross-module ref.
- **II. CQRS + Result Contract** ✅ — `MoveFolderCommand` → `Result` → ProblemDetails; `FolderErrors` gains
  `folder.circular_move`; reuses `folder.not_found` / `folder.max_depth_exceeded` / `folder.duplicate_name`.
- **III. Data Isolation** ✅ — folder + target read through the session filter; the **bulk path `UPDATE` includes
  `user_session_id = @session`** (the raw-SQL caveat). Cross-session move → 404, proven by a Testcontainers test.
- **IV. Test-First** ✅ — Domain (`IsPrefixOf` cycle, subtree-depth math), Application (handler branches),
  Integration (subtree paths, cycle, depth, duplicate, isolation, docs-untouched, chat-scope), Angular (optimistic
  re-nest/rollback, enter-predicate excludes subtree, `onDrop` routing, menu). Red→Green.
- **V. Providers** ✅ — no external call; depth from `FolderOptions.MaxDepth` (no magic number).
- **VI/VII/VIII** ✅ — no time/secret; **no migration** (transactional `UPDATE`s); no startup work.
- **IX. Frontend & Design System** ✅ — CDK drag-drop extended to folders, standalone/OnPush/signals, tokens, the
  rollback via the shared signal-notice, a **"Przenieś do…" menu** for non-pointer parity, ≥360px.

**Result: PASS** — no violations; Complexity Tracking empty.

## Project Structure

### Documentation (this feature)

```text
specs/016-us11-move-folders/
├── plan.md, research.md, data-model.md, quickstart.md
├── contracts/move-folder.md
├── checklists/requirements.md
└── tasks.md   (/speckit-tasks)
```

### Source Code (repository root)

```text
src/RagBook/Modules/Folders/
├── Domain/
│   ├── Folder.cs                      # (maybe) Reparent(newParentId) helper; MoveDepth math lives in the handler
│   ├── IFolderMoveRepository.cs       # GetByIdAsync + MaxSubtreeDepthAsync(pathPrefix) + SiblingExistsAsync(parentId,name) + MoveAsync(...)
│   └── FolderPath.cs                  # reused (IsPrefixOf, Depth, Append)
├── Errors/FolderErrors.cs             # + CircularMove (folder.circular_move, 409)
└── Features/MoveFolder/{MoveFolderCommand,MoveFolderCommandHandler}.cs

src/RagBook.Infrastructure/SharedContext/Persistence/FolderMoveRepository.cs   # transaction: bulk path UPDATE (session-scoped) + parent_id UPDATE
src/RagBook.API/Endpoints/FolderEndpoints.cs   # + PATCH /api/folders/{id}/parent (+ MoveFolderRequest)

src/Web/src/app/
├── core/tree.store.ts                 # + moveFolder(folderId, targetParentId|null): optimistic re-parent + rollback + refresh; + isDescendant/validTargets helpers
└── documents/tree/document-tree.*     # folder rows cdkDrag; onDrop routes by kind; cdkDropListEnterPredicate excludes self+subtree; folder "Przenieś do…" menu

tests/
├── RagBook.Domain.Tests/Folders/FolderMoveTests.cs           # IsPrefixOf cycle, subtree-depth math
├── RagBook.Application.Tests/Folders/MoveFolderHandlerTests.cs  # branches
├── RagBook.Api.IntegrationTests/Folders/MoveFolderEndpointTests.cs  # subtree paths / cycle / depth / duplicate / isolation / docs-untouched / chat-scope
└── src/Web (Karma)                    # tree.store moveFolder + isDescendant; document-tree enter-predicate + routing + menu
```

**Structure Decision**: A Folders vertical slice mirroring the folder CRUD; the transactional bulk rewrite lives
in a narrow `IFolderMoveRepository` (Infrastructure, raw SQL, session-scoped). Cycle = `FolderPath.IsPrefixOf`;
depth = target-depth + subtree-height. Frontend extends the US-10 DnD + `TreeStore` optimistic pattern to folders
(re-parent re-nests by `parentId`; a follow-up refresh corrects paths/depths). No migration.

## Complexity Tracking

*No constitution violations — no entries.*
