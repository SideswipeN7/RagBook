# Phase 1 Data Model — Document Upload (US-04)

## Aggregate: `Document` (extended)

US-05 shipped a minimal `Document` (Id, UserSessionId, SizeBytes, Status, Origin). US-04 extends it with
the upload metadata. New members are settable only through the new upload factory; the US-05
`CreateForQuota` factory stays for the quota tests/seed and leaves the upload fields null/default.

| Property | Type | Notes |
|---|---|---|
| `Id`, `UserSessionId`, `SizeBytes`, `Status`, `Origin` | — | From US-05 (unchanged). |
| `FolderId` | `Guid?` | Target folder, or `null` for root (AC-4). FK → `folders.id`, `ON DELETE RESTRICT`. |
| `FileName` | `string?` | Display name incl. extension, post-suffix (AC-5). Null on US-05-seed rows. |
| `ContentType` | `string?` | Detected canonical type: `application/pdf`/`text/markdown`/`text/plain`. |
| `StoragePath` | `string?` | Opaque key returned by `IFileStorage` — where the blob lives. |
| `UploadedAt` | `DateTimeOffset?` | Stamp (via `TimeProvider`) when the upload was recorded. |
| `ChunkCount` | `int` | Chunks produced by US-06; **0** at upload. |

### Factory & behavior

- `static Result<Document> CreateUpload(long sizeBytes, string fileName, string contentType, Guid? folderId, string storagePath, DateTimeOffset uploadedAt)`
  — builds a `Processing`, `User`-origin document with all upload fields set; guards `sizeBytes > 0`
  (`document.empty_file`) and non-blank name/type/path. (Size/type/emptiness are also gated in the
  handler pre-store; this is the aggregate invariant.)
- `void RenameForSuffix(string newFileName)` — used by the repository's suffix-retry (D5/D6) to bump the
  name to the next free candidate before re-inserting. Internal to the upload path.

## Value object: `FileName` (`Modules/Documents/Domain/FileName.cs`)

Pure name helper. `Parse(raw)` → `Base` + `Extension` (extension = last `.` segment, empty if none).
`Value` recomposes; `WithSuffix(int n)` → `"{Base} ({n}){Extension}"` (n from 1, D6). Keeps suffix
formatting in one tested place; comparison for collisions is case-insensitive.

## Type detection: `FileTypeDetector` + `SupportedFileType`

`SupportedFileType` enumerates the three accepted types with their canonical content-type strings and an
allowed-list string for the error message. `FileTypeDetector.Detect(ReadOnlySpan<byte> head, string
fileName)` → `Result<SupportedFileType>`: `%PDF-` → pdf; else valid-UTF-8-text → md/plain by extension;
else `DocumentErrors.UnsupportedFileType` (D1).

## Seams

- `IFileStorage` (Domain): `Task<string> SaveAsync(Stream content, string suggestedName, ct)`,
  `Task<Stream> OpenReadAsync(string storagePath, ct)`, `Task DeleteAsync(string storagePath, ct)`.
  Impl `LocalFileStorage` over `FileStorageOptions.RootPath` (D3).
- `IDocumentUploadRepository` (Domain): `Task<Result> AddUploadedWithinQuotaAsync(Document, QuotaLimits,
  ct)` — advisory-lock quota admit + insert + file-name suffix-retry (D5). Impl `DocumentUploadRepository`.
- `IFolderFileProbe` (Folders module, US-09): **new** impl `DocumentFolderFileProbe.HasFilesAsync(id)` =
  `EXISTS(documents WHERE folder_id = id)` — **replaces** `NoFolderFilesProbe` (D8, FR-014).

## Event

- `DocumentUploaded(Guid DocumentId) : IEvent` — published in-process after commit; US-06's seam (D8).

## Options: `FileStorageOptions`

Bound from `FileStorage:*`; `RootPath` (config-driven, no hard-coded path). The per-file size limit is
**not** here — it stays `QuotaOptions.MaxFileSizeMb` (US-05).

## Persistence — `documents` table (migration `ExtendDocumentsForUpload`)

Adds columns `folder_id uuid NULL`, `file_name text NULL`, `content_type text NULL`, `storage_path text
NULL`, `uploaded_at timestamptz NULL`, `chunk_count int NOT NULL DEFAULT 0`; a FK `folder_id → folders.id
ON DELETE RESTRICT`; index on `folder_id`.

| Index | Definition | Serves |
|---|---|---|
| `ix_documents_folder_id` | `(folder_id)` | folder file-probe (US-09 AC-5), tree read (US-07). |
| `ux_documents_root_file` | `UNIQUE (user_session_id, LOWER(file_name)) WHERE folder_id IS NULL AND file_name IS NOT NULL` | AC-5 uniqueness at root. |
| `ux_documents_folder_file` | `UNIQUE (folder_id, LOWER(file_name)) WHERE folder_id IS NOT NULL AND file_name IS NOT NULL` | AC-5 uniqueness in a folder. |

The functional/partial indexes are raw SQL in the migration; `DocumentConfiguration` maps the new columns
and the `folder_id` FK.

## Validation rules → requirement trace

| Rule | Enforced where | Requirement |
|---|---|---|
| Type by content (PDF sig / UTF-8 text) | `FileTypeDetector` (domain) | AC-2 / FR-002 |
| Size ≤ per-file max (config) | handler pre-store, `QuotaOptions.MaxFileSizeMb` | AC-3 / FR-003 |
| Empty rejected | handler + `CreateUpload` guard | FR-004 |
| Folder association / root | handler sets `FolderId`; folder repo validates ownership | AC-4 / FR-005 / FR-006 |
| Quota count/total atomic | `AddUploadedWithinQuotaAsync` (advisory lock) | FR-007 |
| Duplicate name → suffix (from 1), race-safe | `FileName` + 2 partial unique indexes + retry | AC-5 / FR-008 |
| Binary outside DB | `IFileStorage` / `LocalFileStorage` | FR-009 |
| Processing record + metadata | `Document.CreateUpload` | FR-010 |
| `DocumentUploaded` published | handler after commit | FR-011 |
| Store-then-record + cleanup | handler compensation | FR-012 |
| Folder-not-empty now covers files | `DocumentFolderFileProbe` | FR-014 / US-09 AC-5 |
