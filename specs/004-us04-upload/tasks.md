# Tasks: Document Upload (Upload dokumentu)

**Input**: Design documents from `specs/004-us04-upload/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/upload-api.md, quickstart.md

**Tests**: Included — the constitution mandates Test-First (Red→Green→Refactor); every behavior lands
via a failing test first, at the cheapest tier that proves it (Domain → Application → Integration).

**Organization**: US-04 extends the existing `Documents` module (US-05) and wires through US-09 folders.
The `Document` extension, `FileName`/`FileTypeDetector`, `IFileStorage`, the upload repository, the
migration, and the folder file-probe swap are Foundational. Stories: US1 = valid upload + folder (AC-1/
AC-4), US2 = type by content (AC-2), US3 = size + empty (AC-3), US4 = duplicate suffix + race (AC-5), US5
= atomic quota on upload (FR-007) + closing US-09 AC-5.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel (different files, no incomplete dependency).
- Paths follow plan.md (`src/RagBook/Modules/Documents`, `src/RagBook.API`, `src/RagBook.Infrastructure`, `src/Web`, `tests/…`).

---

## Phase 1: Setup (storage config)

- [X] T001 Add a `FileStorage` section to `src/RagBook.API/appsettings.json` (`RootPath`) and a dev default; ensure the path is config-driven (no hard-coded path).
- [X] T002 [P] Wire a local blob volume/root for `RagBook.AppHost` (dev) so `FileStorage:RootPath` resolves when running via Aspire.

**Checkpoint**: Solution builds; storage root is configurable.

---

## Phase 2: Foundational (Document extension + seams + persistence — BLOCKS all stories)

### Domain (Red → Green)

- [X] T003 [P] Domain test (Red): `FileTypeDetector` — `Should_DetectPdf_When_PdfSignature`, `Should_RejectExeRenamedPdf_When_Detecting` (AC-2), `Should_ClassifyMarkdownVsPlain_When_TextByExtension`, `Should_RejectBinary_When_TextExtensionButNotUtf8` in `tests/RagBook.Domain.Tests/Documents/FileTypeDetectorTests.cs`.
- [X] T004 [P] Domain test (Red): `FileName` — `Should_SplitBaseAndExtension`, `Should_ProduceNumberedSuffixFromOne_When_Deduplicating` (AC-5), `Should_HandleNameWithoutExtension` in `tests/RagBook.Domain.Tests/Documents/FileNameTests.cs`.
- [X] T005 [P] Domain test (Red): `Document.CreateUpload` — `Should_CreateProcessingUploadDocument_When_Valid`, `Should_ReturnEmptyFile_When_SizeZero` (FR-004), sets folder/name/type/path/uploadedAt — in `tests/RagBook.Domain.Tests/Documents/DocumentUploadTests.cs`.
- [X] T006 Implement `SupportedFileType` (pdf/text-plain/text-markdown + allowed-list string) and `FileTypeDetector.Detect(head, fileName)` (`%PDF-` sig; else valid-UTF-8 text → md/plain by extension; else unsupported) in `src/RagBook/Modules/Documents/Domain/` (Green for T003).
- [X] T007 [P] Implement `FileName` value object (`Parse`, `Base`, `Extension`, `Value`, `WithSuffix(n)` → `"name (n).ext"`) in `src/RagBook/Modules/Documents/Domain/FileName.cs` (Green for T004).
- [X] T008 Extend the `Document` aggregate — add `FolderId`, `FileName`, `ContentType`, `StoragePath`, `UploadedAt`, `ChunkCount`; add `CreateUpload(...)` factory (Processing/User, guards non-empty size + non-blank name/type/path) and internal `RenameForSuffix(newName)` — in `src/RagBook/Modules/Documents/Domain/Document.cs` (Green for T005; keep `CreateForQuota`).

### Errors & seams

- [X] T009 [P] Add `DocumentErrors` (`document.unsupported_file_type` Validation — message lists PDF/TXT/MD; `document.empty_file` Validation) in `src/RagBook/Modules/Documents/Errors/DocumentErrors.cs`.
- [X] T010 [P] Define `IFileStorage` (`SaveAsync`, `OpenReadAsync`, `DeleteAsync`) in `src/RagBook/Modules/Documents/Domain/IFileStorage.cs` and `IDocumentUploadRepository.AddUploadedWithinQuotaAsync(Document, QuotaLimits, ct)` in `src/RagBook/Modules/Documents/Domain/`.
- [X] T011 [P] Implement `FileStorageOptions` (`RootPath`, `SectionName="FileStorage"`) in `src/RagBook.Infrastructure/SharedContext/Storage/FileStorageOptions.cs`; bind in `Program.cs`.

### Persistence

- [X] T012 Extend `DocumentConfiguration` (map `folder_id` nullable + FK → `folders.id` `ON DELETE RESTRICT`, `file_name`, `content_type`, `storage_path`, `uploaded_at`, `chunk_count`; `ix_documents_folder_id`) in `src/RagBook.Infrastructure/SharedContext/Persistence/Configurations/DocumentConfiguration.cs`.
- [X] T013 Implement `LocalFileStorage : IFileStorage` (writes under `FileStorageOptions.RootPath`, namespaced `{root}/{sessionId}/{guid}{ext}`) in `src/RagBook.Infrastructure/SharedContext/Storage/LocalFileStorage.cs`; register `AddScoped<IFileStorage, LocalFileStorage>()`.
- [X] T014 Implement `DocumentUploadRepository : IDocumentUploadRepository` — `AddUploadedWithinQuotaAsync` opens the session `pg_advisory_xact_lock` (reuse the US-05 key). Under the lock (which serializes the session's uploads): re-read usage + `QuotaSnapshot.CanAdmit` (quota failure → code, no insert); **compute the first free file name** for the target `(session, folder)` from the existing `LOWER(file_name)` set and `Document.RenameForSuffix(next)` (n from 1); **insert once** (the two partial unique indexes are a backstop, not a retry loop — a same-transaction 23505 retry is wrong on Postgres, research D5) — in `src/RagBook.Infrastructure/SharedContext/Persistence/DocumentUploadRepository.cs`; register it.
- [X] T015 Implement `DocumentFolderFileProbe : IFolderFileProbe` (`HasFilesAsync(folderId)` = `EXISTS(documents WHERE folder_id = id)`) in `src/RagBook.Infrastructure/SharedContext/Persistence/DocumentFolderFileProbe.cs`; **replace** the `NoFolderFilesProbe` registration in `src/RagBook.Infrastructure/DependencyInjection.cs` (FR-014, closes US-09 AC-5).
- [X] T016 Create migration `ExtendDocumentsForUpload` in `src/RagBook.Infrastructure.Migrations` — new columns + `folder_id` FK + `ix_documents_folder_id`, plus raw-SQL `ux_documents_root_file` (`UNIQUE (user_session_id, LOWER(file_name)) WHERE folder_id IS NULL AND file_name IS NOT NULL`) and `ux_documents_folder_file` (`UNIQUE (folder_id, LOWER(file_name)) WHERE folder_id IS NOT NULL AND file_name IS NOT NULL`); applied out-of-band.

**Checkpoint**: Domain green; schema extended; storage + upload repo + real folder probe wired.

---

## Phase 3: User Story 1 — Upload a valid file into a folder or root (AC-1, AC-4) 🎯 MVP

**Independent test**: upload a valid PDF to a folder → 201 `Processing`, `folder_id` set, appears in listing; no folder → root.

- [X] T017 [P] [US1] Application test (Red): `UploadDocumentCommandHandler` — `Should_StoreAdmitAndPublish_When_ValidUpload`, `Should_PlaceAtRoot_When_NoFolder`, `Should_ReturnNotFound_When_TargetFolderInAnotherSession` (FR-006), `Should_DeleteBlob_When_InsertFails` (FR-012) — mocked `IFileStorage`/`IDocumentUploadRepository`/folder repo + `IMessageBus` — in `tests/RagBook.Application.Tests/Documents/UploadDocumentCommandHandlerTests.cs`.
- [X] T018 [US1] Implement `UploadDocumentCommand : ICommand<DocumentResponse>`, `DocumentResponse`, `DocumentUploaded : IEvent`, and `UploadDocumentCommandHandler` (validate empty→type→size; resolve/authorize folder via the folder repo; `IFileStorage.SaveAsync`; `Document.CreateUpload`; `AddUploadedWithinQuotaAsync`; publish `DocumentUploaded`; delete blob on failure) in `src/RagBook/Modules/Documents/Features/UploadDocument/` (Green for T017).
- [X] T019 [US1] Implement `POST /api/documents` (multipart) + `DocumentContracts` in `src/RagBook.API/Endpoints/DocumentEndpoints.cs`; map `MapDocumentEndpoints()` in `Program.cs`.
- [X] T020 [US1] Integration test (Red→Green): `Should_UploadPdfIntoFolder_When_Valid` (201 `Processing`, `folder_id` set) and `Should_PlaceAtRoot_When_NoFolder` in `tests/RagBook.Api.IntegrationTests/Documents/UploadEndpointTests.cs`.
- [X] T021 [P] [US1] Angular `DocumentUploadStore` (signals: `upload(file, folderId)` with progress via `HttpClient` `reportProgress`; refreshes `FolderTreeStore` + `QuotaStore`) in `src/Web/src/app/core/document-upload.store.ts` (+ unit test with `HttpTestingController`).
- [X] T022 [P] [US1] Angular upload component (standalone, OnPush): upload button + drag-drop target on the tree + progress indicator; client pre-validation of type/size (convenience) in `src/Web/src/app/documents/upload/` (+ unit test); surface it in the shell.

**Checkpoint**: AC-1/AC-4 demonstrable — valid upload lands in a folder/root, Processing. MVP.

---

## Phase 4: User Story 2 — Reject unsupported type by signature (AC-2)

**Independent test**: `.exe` renamed `.pdf` → 400 `document.unsupported_file_type`, nothing stored.

- [X] T023 [US2] Integration test (Red→Green): `Should_Reject_When_SignatureMismatch` (exe→pdf → 400 unsupported, no row, no blob) and `Should_Accept_When_GenuineTxtOrMd` in `tests/RagBook.Api.IntegrationTests/Documents/UploadEndpointTests.cs` (domain detection from T006).
- [X] T024 [P] [US2] Angular: pre-validation rejects obviously-unsupported files before upload with a message; extend the upload component + its unit test (server remains the authority).

**Checkpoint**: AC-2 enforced server-side by content, mirrored (best-effort) in the UI.

---

## Phase 5: User Story 3 — Reject oversized and empty files (AC-3, FR-004)

**Independent test**: > per-file limit → `quota.file_too_large`; 0 bytes → `document.empty_file`; both pre-store.

- [X] T025 [US3] Application test (Red→Green): `Should_RejectOversize_When_ExceedsPerFileLimit` and `Should_RejectEmpty_When_ZeroBytes` (handler gates before store) in `tests/RagBook.Application.Tests/Documents/UploadDocumentCommandHandlerTests.cs`.
- [X] T026 [US3] Integration test: `Should_RejectOversize_And_Empty_ServerSide` (bypass client check; assert codes + nothing stored) in `tests/RagBook.Api.IntegrationTests/Documents/UploadEndpointTests.cs`.
- [X] T027 [P] [US3] Angular: pre-validate size against the configured limit before upload; extend the upload component + unit test.

**Checkpoint**: AC-3 + empty enforced server-side; UI pre-checks for UX only.

---

## Phase 6: User Story 4 — Duplicate name auto-suffix (AC-5)

**Independent test**: 2nd `umowa.pdf` in a folder → `umowa (1).pdf`; same name in another folder unsuffixed; concurrent duplicates → distinct names.

- [X] T028 [US4] Integration test (Red→Green): `Should_AutoSuffix_When_DuplicateNameInFolder` (from `(1)`) and `Should_ScopeSuffixPerFolder_When_SameNameDifferentFolders` in `tests/RagBook.Api.IntegrationTests/Documents/UploadEndpointTests.cs`.
- [X] T029 [US4] Integration test (Red): `Should_AvoidCollision_When_TwoDuplicateUploadsRace` — two identical `umowa.pdf` uploads into one folder concurrently → two distinct names (base + `(1)`), no unique-violation surfaced — in `tests/RagBook.Api.IntegrationTests/Documents/UploadConcurrencyTests.cs`.
- [X] T030 [US4] Verify the under-lock free-suffix computation in `DocumentUploadRepository` so T029 is reliably green — the per-session advisory lock serializes the racing uploads, so each computes a distinct free name (base then `(1)`, `(2)`, …); the unique indexes never fire in the happy path (Green).

**Checkpoint**: AC-5 proven — per-folder suffix from `(1)`, race-safe, no overwrite.

---

## Phase 7: User Story 5 — Atomic quota on upload + close US-09 AC-5 (FR-007, FR-014)

**Independent test**: at limit → `quota.exceeded`, nothing stored; two concurrent uploads at 9/10 → at most one; a folder with a file cannot be deleted.

- [X] T031 [US5] Integration test: `Should_RejectUpload_When_QuotaFull` (at document-count limit → `quota.exceeded`, no row/blob) and `Should_LeaveNoOrphan_When_StorageThenFailure` (FR-012) in `tests/RagBook.Api.IntegrationTests/Documents/UploadEndpointTests.cs`.
- [X] T032 [US5] Integration test: `Should_AdmitAtMostOne_When_TwoUploadsRaceAtLimit` (seed 9/10, two concurrent uploads → count ≤ 10) in `tests/RagBook.Api.IntegrationTests/Documents/UploadConcurrencyTests.cs`.
- [X] T033 [US5] Integration test: `Should_BlockFolderDelete_When_FolderHasFile` — upload a file into a folder, then `DELETE /api/folders/{id}` → 409 `folder.not_empty` (US-09 AC-5 closed by `DocumentFolderFileProbe`) — in `tests/RagBook.Api.IntegrationTests/Folders/FolderDeleteBlockedByFileTests.cs`.

**Checkpoint**: quota atomic on the real upload; the US-09 delete-emptiness file arm is live end-to-end.

---

## Phase 8: Docs & polish (cross-cutting)

- [X] T034 Update `README.md` — an "Upload dokumentu" section: supported types + magic-byte/UTF-8 validation, `IFileStorage` (local volume / cloud), the store-then-record + orphan cleanup, per-folder duplicate suffix (from `(1)`), reuse of the US-05 atomic quota admit, `DocumentUploaded` → US-06.
- [X] T035 Record durable knowledge in `AGENTS.md` (upload validates by content not extension; `IFileStorage` local driver + config root; `DocumentUploadRepository` reuses the advisory lock + file-name suffix retry; two partial unique file-name indexes; `NoFolderFilesProbe` replaced by `DocumentFolderFileProbe` — US-09 AC-5 now live; `DocumentUploaded` is the US-06 seam).
- [X] T036 Full green run: `dotnet test RagBook.slnx` (Domain + Application + Testcontainers Integration) and `npm test` in `src/Web`; verify `dotnet run --project src/RagBook.AppHost` starts clean and an upload appears in the tree as Processing.

---

## Dependencies & execution order

- **Setup (T001–T002)** → **Foundational (T003–T016)** block every story.
- **US1 (T017–T022)** is the MVP. **US2 (T023–T024)** reuses the Foundational detector. **US3 (T025–T027)**
  reuses the pre-store gates. **US4 (T028–T030)** relies on the suffix-retry + unique indexes. **US5
  (T031–T033)** reuses the US-05 admit and closes US-09 AC-5 via the probe swap (T015).
- Within a phase, `[P]` tasks touch different files and may run in parallel. Test tasks precede their
  implementation; concurrency (T029, T032) and orphan-cleanup (T031) tests come after their handlers.
- Polish (T034–T036) after all stories are green.

## Parallel example (Foundational)

T003, T004, T005, T009, T010, T011 (`[P]`) touch independent files and can run together; T006/T007/T008
(domain impls) follow their Red tests; T012–T016 (persistence + storage) follow the seams.

## MVP scope

**US1 (T001–T022)** yields a demonstrable increment: drag a PDF onto a folder and see it appear as
Processing. US2–US5 complete type/size/empty validation, duplicate suffixing, atomic quota, and close the
US-09 folder-delete-with-files rule.
