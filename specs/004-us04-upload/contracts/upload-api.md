# Contract — Upload API (US-04)

Session-scoped by the persistence layer (US-01). Failures return RFC 9457 **ProblemDetails** with a
stable `code` (constitution §II). A target `folderId` owned by another session behaves as **404**
(`folder.not_found`), never 403.

## Error codes (this slice + reused)

| Code | ErrorType → HTTP | Meaning |
|---|---|---|
| `document.unsupported_file_type` | Validation → 400 | Content is not a real PDF nor valid text (AC-2). |
| `document.empty_file` | Validation → 400 | 0-byte upload (FR-004). |
| `quota.file_too_large` | Validation → 400 | Over the per-file size limit (AC-3, reused US-05). |
| `quota.exceeded` / `quota.total_size_exceeded` | Conflict → 409 | Count / total-size quota (US-05). |
| `folder.not_found` | NotFound → 404 | `folderId` not in this session (FR-006). |

## POST `/api/documents` — upload (multipart/form-data)

Fields: `file` (the binary part; its filename + declared content type are read but **not trusted** for
validation), `folderId` (optional GUID; omitted/empty → root).

Dispatches `UploadDocumentCommand(FileName, DeclaredContentType, Content, FolderId)` →
`Result<DocumentResponse>`.

- **201 Created** → `DocumentResponse`:

```json
{
  "id": "<guid>",
  "fileName": "umowa (1).pdf",
  "contentType": "application/pdf",
  "sizeBytes": 12345,
  "status": "Processing",
  "folderId": "<guid|null>",
  "uploadedAt": "2026-07-10T14:30:00+00:00"
}
```

- Failures: `document.empty_file` (400), `document.unsupported_file_type` (400 — message lists PDF/TXT/MD),
  `quota.file_too_large` (400), `quota.exceeded`/`quota.total_size_exceeded` (409), `folder.not_found`
  (404). Nothing is stored or recorded on any failure (FR-012).
- On success the response reflects the **post-suffix** `fileName` and the **detected** `contentType`.

## Behavior notes

- Validation order: empty → type (content) → size → folder ownership → store → atomic quota admit +
  insert (with file-name suffix retry) → publish `DocumentUploaded` (research D2/D5).
- Duplicate name in the target folder → auto-suffix `name (n).ext` from `n=1`; a concurrent duplicate
  gets the next free suffix (never a collision).
- The document appears in the tree read (US-07 / current folder+document listing) in `Processing` state.

## Internal seams (not HTTP)

- `IFileStorage` — `SaveAsync(stream, suggestedName)` → storage path; `OpenReadAsync`; `DeleteAsync`
  (used for orphan cleanup). Local driver over `FileStorage:RootPath`.
- `IDocumentUploadRepository.AddUploadedWithinQuotaAsync(Document, QuotaLimits)` — advisory-lock quota
  admit + insert + file-name suffix retry (reuses the US-05 lock).
- `IFolderFileProbe` — now `DocumentFolderFileProbe`: `HasFilesAsync(folderId)` =
  `EXISTS(documents WHERE folder_id = folderId)`, so US-09 folder delete blocks non-empty-of-files.
- `DocumentUploaded(DocumentId) : IEvent` — the US-06 processing seam; no US-04 subscriber.
