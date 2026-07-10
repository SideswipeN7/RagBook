# Implementation Plan: Document Upload (Upload dokumentu)

**Branch**: `004-us04-upload` (spec dir `004-us04-upload`) | **Date**: 2026-07-10 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/004-us04-upload/spec.md`

## Summary

US-04 adds the real **upload** slice to the existing `Documents` module: a `POST /api/documents`
multipart endpoint that **validates** an uploaded file (content-signature type → size → empty),
**stores** the binary outside the relational DB via a new **`IFileStorage`** abstraction, **atomically
admits** it against the US-05 quota, persists the now-extended **`Document`** in `Processing` state, and
publishes a **`DocumentUploaded`** event for US-06. It also **extends the `documents` table** with
`folder_id` (nullable FK → US-09 folders), `file_name`, `content_type`, `storage_path`, `uploaded_at`,
`chunk_count`, and — critically — **replaces `NoFolderFilesProbe` with a real `folder_id` query**, so
US-09's delete-emptiness file arm becomes live (FR-014, closing US-09 AC-5 end-to-end).

Technical approach: type validation is a pure `FileTypeDetector` (PDF by `%PDF-` signature; non-PDF
must be valid UTF-8 text → classified `.md`→`text/markdown` else `text/plain`), so AC-2 is domain/unit
tested without a server. The per-file/size/empty guards reuse the US-05 `QuotaOptions.MaxFileSizeMb`
(config-driven). The upload handler orchestrates **store-then-record**: it resolves the target folder
(cross-session → `folder.not_found`/404), stores the bytes, then calls the extended repository seam
which — **under the existing session advisory lock** — re-checks quota and inserts the `Document`,
**retrying the file-name suffix on the `(folder_id, LOWER(file_name))` unique-index violation** so
duplicate names auto-suffix (`name (n).ext`, n from **1**) and cannot collide under concurrency. If
storage succeeds but the insert ultimately fails, the stored blob is deleted (no orphans, FR-012). The
frontend adds an upload button + drag-and-drop with client pre-validation and a progress indicator.

## Technical Context

**Language/Version**: C# (LangVersion `preview`) on **.NET 10**; TypeScript (Angular latest stable)

**Primary Dependencies**: ASP.NET Core (multipart), **Wolverine** (dispatch + `DocumentUploaded`
publish), EF Core + Npgsql, `Microsoft.Extensions.Options`, .NET Aspire, Angular standalone/signals.
No new heavy dependency: PDF parsing is US-06's concern — US-04 only reads the leading signature bytes.

**Storage**: PostgreSQL — the `documents` table gains `folder_id` (NULL, FK → `folders.id`,
`ON DELETE RESTRICT`), `file_name`, `content_type`, `storage_path`, `uploaded_at`, `chunk_count`, plus
two **partial unique indexes** on file name (root vs foldered, `LOWER(file_name)`, `WHERE file_name IS
NOT NULL`). Binary content lives **outside** the DB via `IFileStorage` (a local mounted-volume driver
now; a cloud object-storage driver is a later infra swap behind the same interface).

**Testing**: xUnit + FluentAssertions across three tiers; **Testcontainers** PostgreSQL for the
integration tier (AC-1 upload+folder, AC-2 magic-bytes reject, AC-3 size/empty, AC-4 folder_id, AC-5
suffix + concurrent-duplicate race, quota-exceeded, cross-session 404, orphan-cleanup, folder-not-empty
now blocks). Angular unit tests for the upload store/component.

**Target Platform**: Linux container → GCP Cloud Run (stateless API; blobs in a mounted volume locally /
object storage in prod); modern browsers for the SPA.

**Project Type**: Web application (Angular SPA + ASP.NET Core modular monolith), Aspire-orchestrated.

**Performance Goals**: Not a US-04 driver. Upload is one buffered read + one signature check + one
blob write + one advisory-locked insert. Files are bounded by the quota per-file limit.

**Constraints**: Validate by **content, not extension/declared type**; server is the size authority;
all limits config-driven (US-05 `QuotaOptions`); errors via `Result<T>` → ProblemDetails with stable
`document.*`/`quota.*`/`folder.*` codes, never a naked 500; isolation inherited from US-01/US-09
(cross-session folder → 404); **store-then-record with orphan cleanup**; migrations out-of-band.

**Scale/Scope**: Case-study scale. US-04 delivers upload + storage abstraction + Document extension +
the real folder file-probe + the upload UI. Explicitly **not**: chunking/embeddings and ready/failed
transitions (US-06), multi-file/bulk upload (US-12), OCR/DOCX/images.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | US-04 compliance |
|---|---|
| **I. Vertical-slice modular monolith** | New `Documents/Features/UploadDocument/` slice + `Documents/Storage/` seam; extends the existing `Document` aggregate. `DocumentUploaded` is an in-process `IEvent` — US-06 subscribes without a direct reference (no cross-module calls). ✅ |
| **II. CQRS + Result contract** | `UploadDocumentCommand : ICommand<DocumentResponse>`; `DocumentErrors` catalog (`document.unsupported_file_type`, `document.empty_file`, plus reused `quota.*`, `folder.not_found`); the `DocumentsExceptionHandler` maps the file-name `23505` to the suffix-retry (not a naked error). `Permissions/` deferred — see Complexity Tracking. ✅ (justified deviation) |
| **III. Data isolation by session** | `Document` stays `ISessionOwned`; the target `folder_id` is validated through the session-filtered folder repository (cross-session → `folder.not_found`/404); no document is ever read or written for another session. ✅ |
| **IV. Test-first (Red→Green→Refactor)** | Domain (`FileTypeDetector` signatures/UTF-8 = AC-2, `FileName` suffix logic = AC-5, `Document.CreateUpload` invariants), Application (upload handler with mocked storage/quota/folder seams; empty/size/type gates), Integration (Testcontainers: AC-1..AC-5, quota, 404, orphan cleanup, folder-not-empty). ✅ |
| **V. Provider resilience + cache** | `IFileStorage` is an infra seam with a local driver; no external AI provider in US-04 (that is US-06). Storage failures are handled (cleanup), not cached. ✅ |
| **VI. Auditing & time** | `Document` is `IAuditable`; `uploaded_at`/audit stamped centrally via `TimeProvider` — never `DateTime.UtcNow`. ✅ |
| **VII. Secrets** | No secrets. The per-file limit is `QuotaOptions.MaxFileSizeMb` (config-driven); the storage root is configuration, not a hard-coded path. ✅ |
| **VIII. Operations & delivery** | Migration `ExtendDocumentsForUpload` in `RagBook.Infrastructure.Migrations`, applied out-of-band. The local blob volume is wired via config/AppHost; no startup migration. ✅ |
| **IX. Frontend & design system** | Angular standalone upload control + drag-and-drop (OnPush, signals), progress state, client pre-validation (convenience only), design tokens, shared error surface; the tree shows the new document in a processing state via the existing `FolderTreeStore`/document read. ✅ |

**Gate result: PASS** with one justified deviation (`Permissions/` folder still deferred — anonymous sessions).

## Project Structure

### Documentation (this feature)

```text
specs/004-us04-upload/
├── plan.md              # This file
├── research.md          # Phase 0 — decisions (type detection, storage seam, store-then-record, suffix+race, event)
├── data-model.md        # Phase 1 — Document extension, FileName VO, IFileStorage, DDL + indexes
├── quickstart.md        # Phase 1 — run & validate AC-1..AC-5
├── contracts/
│   └── upload-api.md     # Phase 1 — POST /api/documents + storage & event seam contracts
├── checklists/
│   └── requirements.md  # spec quality checklist
└── tasks.md             # Phase 2 — /speckit-tasks output
```

### Source Code (repository root) — new/changed for US-04

```text
src/
├── RagBook/                                          # Core
│   └── Modules/Documents/
│       ├── Domain/
│       │   ├── Document.cs                            # + FolderId, FileName, ContentType, StoragePath, UploadedAt, ChunkCount; + CreateUpload(...) factory
│       │   ├── FileName.cs                            # value object: base+ext parse, WithSuffix(n) → "name (n).ext"
│       │   ├── FileTypeDetector.cs                    # %PDF- signature + UTF-8 text classification (.md/.txt) → content type or unsupported
│       │   ├── SupportedFileType.cs                   # pdf/text-plain/text-markdown + allowed-list for the error message
│       │   ├── IFileStorage.cs                        # SaveAsync(stream)→storagePath, OpenReadAsync, DeleteAsync (seam)
│       │   └── IDocumentUploadRepository.cs           # AddUploadedWithinQuotaAsync(Document, QuotaLimits, ct) — advisory lock + quota + suffix-retry
│       ├── Errors/
│       │   └── DocumentErrors.cs                      # document.unsupported_file_type, document.empty_file (+ reuse quota.*/folder.not_found)
│       ├── Storage/                                   # (Core holds only the abstraction; impl in Infrastructure)
│       └── Features/UploadDocument/
│           ├── UploadDocumentCommand.cs               # ICommand<DocumentResponse> (stream, fileName, declaredContentType, folderId)
│           ├── UploadDocumentCommandHandler.cs        # validate → resolve folder → store → admit+insert(+suffix retry) → publish → cleanup
│           ├── DocumentResponse.cs                    # id, fileName, contentType, sizeBytes, status, folderId, uploadedAt
│           └── DocumentUploaded.cs                    # IEvent (DocumentId) — US-06 seam
├── RagBook.API/
│   ├── Program.cs                                     # + Configure<FileStorageOptions>; MapDocumentEndpoints()
│   ├── appsettings.json                               # + "FileStorage": { "RootPath": "..." }
│   └── Endpoints/
│       ├── DocumentEndpoints.cs                       # POST /api/documents (multipart/form-data)
│       └── DocumentContracts.cs                       # multipart binding / response DTO
├── RagBook.Infrastructure/
│   ├── DependencyInjection.cs                         # + IFileStorage→LocalFileStorage; IFolderFileProbe→DocumentFolderFileProbe (REPLACES NoFolderFilesProbe); IDocumentUploadRepository
│   └── SharedContext/
│       ├── Persistence/
│       │   ├── Configurations/DocumentConfiguration.cs # + new columns, folder_id self-scope FK, (session-level) file-name index mapping
│       │   ├── DocumentUploadRepository.cs             # advisory-lock quota admit + insert + file-name suffix retry on 23505
│       │   └── DocumentFolderFileProbe.cs              # IFolderFileProbe: EXISTS(documents WHERE folder_id = id) — closes US-09 AC-5
│       └── Storage/
│           ├── LocalFileStorage.cs                     # IFileStorage over a configured root volume
│           └── FileStorageOptions.cs                   # RootPath (config-driven)
├── RagBook.Infrastructure.Migrations/Migrations/      # ExtendDocumentsForUpload (columns + folder_id FK + 2 partial unique file-name indexes)
└── Web/src/app/
    ├── core/document-upload.store.ts                 # signals: upload(file, folderId) with progress; refreshes tree/quota
    └── documents/upload/                              # upload button + drag-drop target + progress (standalone, OnPush)
tests/
├── RagBook.Domain.Tests/Documents/                    # FileTypeDetectorTests, FileNameTests, DocumentUploadTests
├── RagBook.Application.Tests/Documents/               # UploadDocumentCommandHandlerTests (mocked storage/quota/folder)
└── RagBook.Api.IntegrationTests/Documents/            # UploadEndpointTests (AC-1..AC-5, quota, 404, orphan), UploadConcurrencyTests (dup race), FolderDeleteBlockedByFileTests (US-09 AC-5 closed)
```

**Structure Decision**: US-04 fills the `Documents` module's `Features/UploadDocument/` slice and its
`Storage/` seam, extends the existing `Document` aggregate/table, and swaps the folder file-probe
implementation. It reuses the US-05 advisory-lock quota admit rather than re-implementing atomicity, and
the US-09 folder repository for target validation.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| `Documents` module still ships **no `Permissions/`** (§II) | Sessions are anonymous; upload applies uniformly to the owning session — nothing to authorize beyond session ownership (already enforced by the query filter). | Empty scaffolding; re-introduced by the first story with a real permission surface (same as US-01/US-05/US-09). |
| A **new `IDocumentUploadRepository`** rather than extending `IDocumentQuotaRepository.TryAddWithinQuotaAsync` | Upload needs the advisory-lock quota admit **plus** file-name **suffix-retry** on the `(folder_id, LOWER(file_name))` unique violation — a distinct concern from US-05's pure quota admit. Keeping US-05's seam untouched avoids regressing its tests. | Overloading the US-05 method would entangle quota conflict mapping with name-collision retry and change a shipped, tested contract. |
| Two **partial unique file-name indexes** (root vs foldered), both `WHERE file_name IS NOT NULL` | Per-folder uniqueness with a nullable `folder_id` (root) hits the same NULL-distinct problem as folders; the `file_name IS NOT NULL` predicate excludes US-05's minimal seed documents (no file) from the constraint. | A single composite unique index would neither guard root duplicates nor tolerate the pre-existing file-less rows. |

## Phase notes

- **Phase 0 (research.md)** — decisions: content-signature type detection (PDF magic bytes + UTF-8 text
  classification), `IFileStorage` seam + local driver + path layout, store-then-record ordering with
  orphan cleanup, reuse of the US-05 advisory-lock admit extended with file-name suffix-retry, the two
  partial unique file-name indexes, `DocumentUploaded` as an in-process `IEvent`, replacing
  `NoFolderFilesProbe`.
- **Phase 1 (data-model.md, contracts/, quickstart.md)** — the extended `Document` + `FileName` VO +
  `FileTypeDetector`; `IFileStorage`/`FileStorageOptions`; the `POST /api/documents` contract + storage
  & event seam contracts + the migration DDL; the runnable quickstart proving AC-1..AC-5.
- **Phase 2 (tasks.md)** — produced by `/speckit-tasks`, Red→Green→Refactor per tier, with the
  concurrent-duplicate race and orphan-cleanup integration tests last.
