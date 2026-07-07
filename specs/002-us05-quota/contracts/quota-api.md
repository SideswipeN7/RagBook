# Phase 1 Contracts — US-05 Quota API & seams

All endpoints run behind `SessionMiddleware` (US-01): every response carries/refreshes the session
cookie, and the quota is always scoped to the current session. Errors follow RFC 9457 ProblemDetails
with a stable `code`.

## GET /api/quota — current quota state (AC-1)

- **Purpose**: the read the quota-bar renders; reports usage and the configured limits for the current
  session.
- **Request**: no body. Cookie resolved/issued by the middleware.
- **Response `200`**:
  ```json
  {
    "usedDocuments": 7,
    "maxDocuments": 10,
    "usedMb": 11.8,
    "maxTotalMb": 50,
    "maxFileSizeMb": 10,
    "canUpload": true
  }
  ```
  - `usedDocuments` — count of the session's documents excluding `Demo` origin; `Failed` included.
  - `usedMb` — `round(usedBytes / 1_000_000, 1)` (decimal MB).
  - `canUpload` — `usedDocuments < maxDocuments && usedBytes < maxTotalBytes`.
- **Fresh session**: `{ "usedDocuments": 0, "usedMb": 0, "canUpload": true, ... }`.

## Internal seam — admit a document within quota (AC-2/AC-3/AC-5)

> Not an HTTP endpoint in US-05 (no upload UI yet). This is the seam **US-04's upload command** calls;
> US-05 builds and tests it. Exposed as `IQuotaService.TryAdmitAsync` → `IDocumentQuotaRepository
> .TryAddWithinQuotaAsync`.

- **Behavior**: within one transaction, take a session-keyed advisory lock, re-read count + size sum
  (excluding `Demo`), evaluate `QuotaSnapshot.CanAdmit(sizeBytes)`, then either insert a `Processing`
  document and return its id, or return a failure **without inserting**:
  - file over the per-file max → `quota.file_too_large` (→ 400)
  - count at/over `MaxDocuments` → `quota.exceeded` (→ 409)
  - total would cross `MaxTotalMb` → `quota.total_size_exceeded` (→ 409), message conveys remaining space
- **Atomicity (AC-5)**: two concurrent admits for the same session serialize on the advisory lock; at
  most one is admitted, final count ≤ `MaxDocuments`.

### Error codes (module catalog `QuotaErrors`)

| Code | ErrorType | HTTP | When |
|---|---|---|---|
| `quota.exceeded` | Conflict | 409 | admitting would exceed `MaxDocuments` |
| `quota.total_size_exceeded` | Conflict | 409 | admitting would exceed `MaxTotalMb` (message states remaining MB) |
| `quota.file_too_large` | Validation | 400 | file larger than `MaxFileSizeMb` |
| `quota.conflict` | Conflict | 409 | infra fallback (unique/concurrency) via `DocumentsExceptionHandler` |

## Cross-cutting

- **Pre-check**: `IQuotaService.CheckCanUpload(sizeBytes)` returns the same codes without inserting —
  the cheap guard the upload endpoint (US-04) calls before reading a large body; the authoritative
  check remains the in-lock admit.
- **Isolation**: all counts/sums flow through the US-01 global query filter — one session's quota never
  reflects another's documents.
- **Frontend**: the `notFoundInterceptor` (US-01) is unchanged; the quota codes surface to the SPA via
  ProblemDetails `code`, which the upload UI (US-04) branches on to show the "quota full — delete files"
  message and disable the button. US-05 ships the `quota-bar` + `QuotaStore.refresh()` that back this.
