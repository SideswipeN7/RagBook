# Phase 0 Research — US-10 Przenoszenie plików (drag & drop)

## D1 — Backend move: slice + repository shape

**Decision**: `MoveDocumentCommand(Guid DocumentId, Guid? TargetFolderId) : ICommand` → `Result`, handled by
`MoveDocumentCommandHandler(IDocumentMoveRepository repository, IFolderReference folders)`. New narrow
`IDocumentMoveRepository { Task<Document?> GetByIdAsync(Guid id, ct); Task SaveChangesAsync(ct); }` (EF impl reads
through the session filter → tracked entity; save persists `MoveToFolder`).

**Rationale**: Mirrors the `DeleteDocument` slice + narrow-repository convention. `IFolderReference` already exists
as the Documents-owned, session-scoped folder-existence seam (used by upload), so no Folders-module reference.

**Handler order**: get document → `null` → `document.not_found`; `Origin == Demo` → `document.read_only`; if
`TargetFolderId is Guid f` and `!await folders.ExistsInSessionAsync(f)` → `folder.not_found`; if
`document.FolderId == TargetFolderId` → **no-op** `Result.Success()` (no save); else `document.MoveToFolder(f)` +
`SaveChangesAsync`.

**Alternatives rejected**: Reusing a broad document repository (none exposes a tracked get + save for this);
referencing the Folders module directly (violates §I — `IFolderReference` is the seam).

## D2 — `document.read_only` error type

**Decision**: Add `DocumentErrors.ReadOnly = Error.Conflict("document.read_only", …)` (→ 409). A demo document is
a valid resource whose state simply can't be mutated — a conflict with its read-only nature, not a 404 (it exists)
nor a 400 (the request is well-formed).

**Rationale**: 409 matches the constitution's conflict mapping and reads correctly to the client. Demo documents
arrive with US-03 (not built yet); the guard is implemented + tested now (seed a `DocumentOrigin.Demo` row) so the
move action is complete.

**Alternatives rejected**: 403 (this isn't an authorization decision — the resource is the user's own session's);
skipping the guard until US-03 (would ship an incomplete move action).

## D3 — Endpoint verb/shape

**Decision**: `PATCH /api/documents/{id}/folder`, body `{ "folderId": <guid|null> }` (`MoveDocumentRequest(Guid?
FolderId)`); `Result` → 204 / ProblemDetails.

**Rationale**: PATCH on a sub-resource ("the document's folder") is the natural partial update; `null` = root. Keeps
the existing `POST`/`DELETE /api/documents` shape and adds one route.

## D4 — Optimistic move + rollback in `TreeStore`

**Decision**: `TreeStore` holds the raw fetched `documents`/`folders` in signals and composes `roots` from them.
Add `moveDocument(documentId, targetFolderId: string | null)`:
1. Read the document's current `folderId` (for rollback) and short-circuit if it already equals the target (no-op).
2. **Optimistically** set the document's `folderId` to the target in the local signal (the tree recomposes at once).
3. `PATCH /api/documents/{id}/folder`; on success keep it, on error **revert** the local `folderId` and surface a
   design-system notice with the reason.

**Rationale**: `TreeStore` owns the tree state, so the optimistic/rollback logic is unit-testable without the DOM
(the story's real risk). The component only wires drag/menu events to `moveDocument`.

**Alternatives rejected**: Refresh-after-move (loses the instant feel — the story's whole point); optimistic logic
in the component (couples it to the tree shape and is harder to test).

## D5 — Drag-and-drop mechanics (`@angular/cdk/drag-drop`)

**Decision**: Document rows are `cdkDrag` (payload = the document). Folder nodes and a dedicated **root drop-zone**
are `cdkDropList`, connected via `cdkDropListGroup`; a `(cdkDropListDropped)` on a target resolves its folder id
(or `null` for root) and calls `moveDocument`. Target highlight via `cdkDropListEntered`/`Exited` (a CSS token
class). Invalid targets (the dragged doc, a demo section) are simply not drop lists.

**Rationale**: The tree already uses `@angular/cdk`; drag-drop is the same package (no new dependency). Per-target
drop lists give precise "drop onto folder/root" semantics and free enter/exit highlight hooks.

**Alternatives rejected**: Native HTML5 DnD (more boilerplate, weaker a11y story, no built-in preview); a
reorder-style single list (documents move **between** containers, not reorder within one).

## D6 — Context-menu "Przenieś do…" fallback

**Decision**: A per-document menu (design-system, keyboard-reachable) lists the session's folders + a "Root"
option; selecting one calls the **same** `moveDocument` — identical effect to a drop (SC-004). Folders come from
`TreeStore`'s folder list.

**Rationale**: Drag-and-drop must not be the only path (§IX / FR-008). Reusing `moveDocument` guarantees parity by
construction. A flat folder list is adequate for the MVP corpus (≤ a few dozen folders).

**Alternatives rejected**: A separate move endpoint/flow for the menu (would risk drift from the drag path).

## D7 — Error surface (the "toast")

**Decision**: Reuse the project's **signal-notice** pattern (as `NotFoundNotifier` → `shell__notice`): the tree
holds a `moveError` signal (or a small shared notifier) rendered as a design-system notice (`role="alert"`,
dismissible/auto-clearing). No native `alert`.

**Rationale**: Consistent with the existing 404 notice; tokens only; testable via the signal. A full toast-stack
component is out of scope — one notice conveys the rollback reason (FR-002).

## D8 — Chunks untouched (proof)

**Decision**: The move only writes `folder_id`; an integration test asserts the document's **chunk count/content is
identical before and after** the move (SC-003), demonstrating the vector index is not rebuilt.

**Rationale**: The "folder as attribute" advantage is a headline point; a test pins it so a future change that
accidentally couples folders to indexing is caught.
