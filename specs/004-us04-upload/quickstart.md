# Quickstart — Validate US-04

## Prerequisites

- .NET 10 SDK, Node.js (Angular), Docker running (integration tests / Aspire PostgreSQL).
- `dotnet tool restore` before creating/applying migrations (see AGENTS.md).
- A writable blob root for local storage (`FileStorage:RootPath`); the AppHost/config points at a temp
  volume in development.

## Run locally (Aspire)

```sh
cd src/Web && npm install && cd -
dotnet run --project src/RagBook.AppHost
# Upload via the SPA: drag a PDF/TXT/MD onto a folder (or the root). It appears in the tree as
# "Processing"; background chunking/embeddings arrive in US-06.
```

## Automated validation (the source of truth for DoD)

```sh
# Cheapest tiers first (no Docker)
dotnet test tests/RagBook.Domain.Tests            # FileTypeDetector (AC-2), FileName suffix (AC-5), CreateUpload invariants
dotnet test tests/RagBook.Application.Tests        # UploadDocumentCommandHandler (mocked storage/quota/folder seams)

# Integration tier — START DOCKER FIRST (Testcontainers PostgreSQL + local blob volume)
dotnet test tests/RagBook.Api.IntegrationTests     # AC-1..AC-5, quota, 404, orphan cleanup, folder-not-empty

# Frontend
cd src/Web && npm test                             # upload store/component, drag-drop, progress, pre-validation
```

Tests map to acceptance criteria:

| AC | Tier | Test (`Should_..._When_...`) | Proves |
|---|---|---|---|
| AC-2 | Domain | `Should_RejectExeRenamedPdf_When_Detecting` | no `%PDF-`, not text → `document.unsupported_file_type` |
| AC-2 | Domain | `Should_ClassifyMarkdownVsPlain_When_TextByExtension` | `.md`→text/markdown, else text/plain; binary `.txt`→unsupported |
| AC-5 | Domain | `Should_ProduceNumberedSuffixFromOne_When_Deduplicating` | `FileName.WithSuffix(1)` → `umowa (1).pdf` |
| FR-004 | Domain | `Should_ReturnEmptyFile_When_SizeZero` | `CreateUpload(0,…)` → `document.empty_file` |
| AC-1 | Application | `Should_StoreAdmitAndPublish_When_ValidUpload` | store → admit → `Document(Processing)` → `DocumentUploaded` |
| AC-3 | Application | `Should_RejectOversize_When_ExceedsPerFileLimit` | `quota.file_too_large`, nothing stored |
| FR-006 | Application | `Should_ReturnNotFound_When_TargetFolderInAnotherSession` | folder repo returns null → `folder.not_found` |
| FR-012 | Application | `Should_DeleteBlob_When_InsertFails` | storage cleanup on admit/insert failure (no orphan) |
| AC-1/AC-4 | Integration | `Should_UploadPdfIntoFolder_When_Valid` | 201 `Processing`; `folder_id` set; appears in listing |
| AC-2 | Integration | `Should_Reject_When_SignatureMismatch` | `.exe`→`.pdf` → 400 unsupported, nothing persisted/stored |
| AC-5 | Integration | `Should_AutoSuffix_When_DuplicateNameInFolder` | 2nd `umowa.pdf` → `umowa (1).pdf`; other folder unsuffixed |
| **AC-5 race** | Integration | `Should_AvoidCollision_When_TwoDuplicateUploadsRace` | two `umowa.pdf` concurrently → two distinct names |
| FR-007 | Integration | `Should_RejectUpload_When_QuotaFull` | at limit → `quota.exceeded`, nothing stored |
| FR-012 | Integration | `Should_LeaveNoOrphan_When_StorageThenFailure` | forced failure → 0 rows, 0 blobs, quota unchanged |
| **US-09 AC-5** | Integration | `Should_BlockFolderDelete_When_FolderHasFile` | upload into folder → `DELETE /api/folders/{id}` → 409 `folder.not_empty` |
| FR-008 | Integration | `Should_ScopeSuffixPerFolder_When_SameNameDifferentFolders` | same name in two folders → both unsuffixed |

## Manual smoke (optional)

```sh
# valid upload to root
curl -i -c jar -b jar -F "file=@sample.pdf;type=application/pdf" https://localhost:<api>/api/documents
# → 201 { "fileName": "sample.pdf", "status": "Processing", "folderId": null, ... }
# upload the same name again → "sample (1).pdf"
# rename sample.exe → sample.pdf and upload → 400 document.unsupported_file_type
```

## Expected outcomes

- A valid PDF/TXT/MD is stored, recorded `Processing`, associated with the chosen folder (or root), and
  a `DocumentUploaded` event is published.
- Type (by content), size, and empty checks reject before anything is stored; the server is the size
  authority.
- Duplicate names auto-suffix per folder from `(1)`, race-safe; nothing is overwritten.
- Quota is enforced atomically; a failed store/insert leaves no orphan file or row.
- Deleting a folder that now contains a file is blocked with `folder.not_empty` (US-09 AC-5 closed).
