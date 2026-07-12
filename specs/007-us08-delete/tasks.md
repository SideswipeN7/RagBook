# Tasks: Delete Document (Usuwanie dokumentu)

**Input**: Design documents from `specs/007-us08-delete/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/delete-api.md, quickstart.md

**Tests**: Included — the constitution mandates Test-First (Red→Green→Refactor); every behavior lands
via a failing test first, at the cheapest tier that proves it (Application → Integration; Web unit).

**Organization**: Small story on a mature base — no schema change (the chunks cascade FK exists from
US-06). One Foundational slice (delete command + seam + endpoint), then the stories: US1 = delete + confirm
+ refresh (AC-1), US2 = cascade (AC-2), US3 = delete during processing (AC-3), US4 = ownership 404 (AC-4),
US5 = clean-404 for a deleted id (AC-5, forward-looking).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel (different files, no incomplete dependency).
- Paths follow plan.md (`src/RagBook/Modules/Documents`, `src/RagBook.API`, `src/RagBook.Infrastructure`, `src/Web`, `tests/…`).

---

## Phase 1: Setup

- [X] T001 Add `DocumentErrors.NotFound` (`document.not_found`, NotFound → 404) to `src/RagBook/Modules/Documents/Errors/DocumentErrors.cs`.

**Checkpoint**: Solution builds; the not-found code exists.

---

## Phase 2: Foundational (delete slice — BLOCKS the stories)

- [X] T002 [P] Application test (Red): `DeleteDocumentCommandHandler` — `Should_Delete_When_Present` (repo returns true → `Result.Success`) and `Should_ReturnNotFound_When_DocumentMissing` (repo returns false → `document.not_found`) with a mocked `IDocumentDeletionRepository` — in `tests/RagBook.Application.Tests/Documents/DeleteDocumentCommandHandlerTests.cs`.
- [X] T003 [P] Define `IDocumentDeletionRepository` (`Task<bool> DeleteAsync(Guid documentId, ct)` — session-scoped; false = not found) in `src/RagBook/Modules/Documents/Domain/IDocumentDeletionRepository.cs`.
- [X] T004 Implement `DeleteDocumentCommand(Guid Id) : ICommand` and `DeleteDocumentCommandHandler` (`deleted ? Result.Success() : Result.Failure(DocumentErrors.NotFound)`) in `src/RagBook/Modules/Documents/Features/DeleteDocument/` (Green for T002).
- [X] T005 Implement `DocumentDeletionRepository : IDocumentDeletionRepository` — session-scoped tracked load (null → return false); transactional `Remove` + `SaveChanges` (chunks cascade via the US-06 FK) + commit; then **best-effort** `IFileStorage.DeleteAsync(storagePath)` wrapped in try/catch with an `ILogger` warning; return true — in `src/RagBook.Infrastructure/SharedContext/Persistence/DocumentDeletionRepository.cs`; register `AddScoped<IDocumentDeletionRepository, DocumentDeletionRepository>()` in `src/RagBook.Infrastructure/DependencyInjection.cs`.
- [X] T006 Add `DELETE /api/documents/{id:guid}` to `src/RagBook.API/Endpoints/DocumentEndpoints.cs` (dispatch `DeleteDocumentCommand` → 204, or `ProblemResults.Problem` on failure).

**Checkpoint**: `DELETE /api/documents/{id}` deletes a session-owned document (chunks cascade) or returns 404; application tests green.

---

## Phase 3: User Story 1 — Delete with confirmation + live refresh (AC-1) 🎯 MVP

**Independent test**: delete a present document → 204, its chunks gone, and (frontend) the tree drops the row + the quota drops without a reload.

- [X] T007 [US1] Integration test (Red→Green): `Should_DeletePresentDocument` — seed + index a document, `DELETE /api/documents/{id}` → 204, and the document row is gone — in `tests/RagBook.Api.IntegrationTests/Documents/DeleteDocumentEndpointTests.cs`.
- [X] T008 [P] [US1] Angular `DocumentActionsStore` (`delete(id)` → `DELETE /api/documents/{id}` → on success `TreeStore.refresh()` + `QuotaStore.refresh()`) in `src/Web/src/app/core/document-actions.store.ts` (+ unit test with `HttpTestingController` asserting the DELETE + both refreshes).
- [X] T009 [US1] Add a **Delete** action + inline confirmation to document leaves in `app-document-tree` (reuse the folder-delete confirm pattern; no native dialog), calling `DocumentActionsStore.delete(id)`; render in `document-tree.html` (+ component test: a document leaf shows the action and confirms before deleting). **Dispatch by node kind (C1):** the leaf confirm MUST call `DocumentActionsStore.delete` (`DELETE /api/documents`), NOT the folder delete — use a separate confirm handler/state for document leaves so a folder and a document confirm never cross wires.

**Checkpoint**: AC-1 demonstrable — confirm → delete → tree + quota update without a reload. MVP.

---

## Phase 4: User Story 2 — Deleting removes the whole index (AC-2)

**Independent test**: index a document (N chunks), delete it → zero chunks remain with its id.

- [X] T010 [US2] Integration test (Red→Green): `Should_CascadeDeleteChunks_When_DocumentDeleted` — index a document, `DELETE`, assert `chunks` has no row with its `document_id` — in `DeleteDocumentEndpointTests.cs`.

**Checkpoint**: AC-2 — the index cascades away with the document.

---

## Phase 5: User Story 3 — Delete during processing (AC-3)

**Independent test**: with a processing document, delete it, then run the US-06 handler → still deleted, no chunks, no error.

- [X] T011 [US3] Integration test (Red→Green): `Should_Delete_And_WorkerAbortsQuietly_When_ProcessingDocumentDeleted` — seed a processing document, `DELETE` it (204), then invoke `ProcessDocumentHandler.Handle` directly and assert no chunks were written and no exception surfaced — in `DeleteDocumentEndpointTests.cs`.

**Checkpoint**: AC-3 — deletion wins over in-flight processing; the worker aborts quietly.

---

## Phase 6: User Story 4 & 5 — Ownership 404 + idempotent/clean-404 (AC-4, AC-5)

**Independent test**: session B deleting A's document → 404, untouched; a second delete → 404.

- [X] T012 [US4] Integration test (Red→Green): `Should_Return404_When_DeletingAnotherSessionsDocument` — session A owns a document; session B `DELETE`s it → 404 `document.not_found`; A's document + chunks intact — in `DeleteDocumentEndpointTests.cs`.
- [X] T013 [US5] Integration test (Red→Green): `Should_Return404_When_DeletingTwice` — delete a document (204), delete it again → 404 (idempotent-from-user; a future citation resolves to a clean 404) — in `DeleteDocumentEndpointTests.cs`.

**Checkpoint**: AC-4/AC-5 — isolation and clean, idempotent 404.

---

## Phase 7: Docs & polish (cross-cutting)

- [X] T014 [P] Integration test: `Should_StillDelete_When_BlobRemovalFails` (FR-004) — **replace `IFileStorage` with a throwing stub via the test host** (`WithWebHostBuilder` + `ConfigureTestServices` — `LocalFileStorage.DeleteAsync` is a no-op on a missing file, so a bogus path won't fail), then seed+index a document and `DELETE` it → still **204** and its record + chunks are gone (the orphan-blob failure is swallowed + logged, not surfaced) — in `DeleteDocumentEndpointTests.cs`.
- [X] T015 Update `README.md` (US-08 note in the documents section: hard delete, **chunks cascade at the DB** via the FK, **DB-first then best-effort blob** ordering with the orphan-on-storage-failure trade-off, session-scoped 404) and record durable knowledge in `AGENTS.md` (`IDocumentDeletionRepository` = tx delete + cascade + best-effort blob + log; delete is 404 for cross-session/repeat; delete-during-processing relies on the US-06 quiet abort).
- [X] T016 Full green run: `dotnet test RagBook.slnx` (Application + Testcontainers Integration) and `npm test` in `src/Web`; verify `dotnet run --project src/RagBook.AppHost` deletes a document and the tree + quota update without a reload.

---

## Dependencies & execution order

- **Setup (T001)** → **Foundational (T002–T006)** block the stories.
- **US1 (T007–T009)** is the MVP (endpoint + row action). **US2 (T010)**, **US3 (T011)**, **US4/US5
  (T012–T013)** are integration assertions over the same delete path.
- Within a phase, `[P]` tasks touch different files and may run in parallel; test tasks precede their
  implementation.
- Polish (T014–T016) after the stories are green.

## Parallel example (Foundational)

T002 and T003 (`[P]`) are independent (test vs seam); T004 (handler) follows the seam + Red test; T005
(repo) + T006 (endpoint) follow.

## MVP scope

**US1 (T001–T009)** yields a demonstrable increment: confirm and delete a document — it disappears from
the tree and the quota drops without a reload, and its index is gone. US2–US5 assert the cascade, the
delete-during-processing behaviour, isolation, and the clean idempotent 404.
