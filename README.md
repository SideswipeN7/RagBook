# RagBook

A case-study RAG assistant over your own documents: upload PDF/TXT/MD → index with pgvector →
ask natural-language questions → stream answers with clickable citations. A **.NET 10** modular
monolith (vertical slices, `Result<T>`, Wolverine) paired with an **Angular** SPA, orchestrated by
**.NET Aspire**, backed by **PostgreSQL + pgvector**, deployed to **GCP Cloud Run**.

This repository currently implements **US-01 — user session (data isolation)** and **US-05 — file
quota** plus the greenfield solution foundation. See `docs/features/` for the full story map and
`.specify/` for the spec-driven artifacts (constitution, spec, plan, tasks).

## Solution layout

| Project | Responsibility |
|---|---|
| `src/RagBook` | Core: domain + application. `Modules/<Module>/` → `Domain/` + `Features/`; per-module `Errors/`. |
| `src/RagBook.API` | Transport: endpoints, session middleware, DI composition, ProblemDetails mapping. |
| `src/RagBook.Infrastructure` | EF Core persistence, session context, interceptors (`SharedContext/`). |
| `src/RagBook.Infrastructure.Migrations` | EF Core migrations only. |
| `src/RagBook.AppHost` | .NET Aspire orchestration (PostgreSQL + API + Angular dev server). |
| `src/RagBook.ServiceDefaults` | Shared telemetry/health/resilience (`AddServiceDefaults()`). |
| `src/Web` | Angular SPA shell (standalone, signals, OnPush). |
| `tests/*` | Domain / Application / Api.IntegrationTests (Testcontainers). |

## Build & test

```sh
dotnet build RagBook.slnx
dotnet test  tests/RagBook.Domain.Tests        # pure domain, no Docker
dotnet test  tests/RagBook.Application.Tests    # handlers/validators, no Docker
dotnet test  tests/RagBook.Api.IntegrationTests # Testcontainers PostgreSQL — START DOCKER FIRST
```

## Run locally

```sh
cd src/Web && npm install && cd -             # install SPA deps once (prerequisite for the web resource)
dotnet run --project src/RagBook.AppHost      # Aspire starts PostgreSQL + the API + the Angular dev server (Docker required)
```

Migrations are created in `src/RagBook.Infrastructure.Migrations` and applied out-of-band (a bundle
or init step) — **never at application startup**.

## Izolacja danych (data isolation)

RagBook has **no login** in the MVP. Every visitor gets an anonymous **`UserSessionId` (GUID v4)** on
their first request, carried in a cookie that is **`HttpOnly`, `Secure`, `SameSite=Strict`** with a
**30-day sliding expiry** (refreshed on every visit). All cookie tunables are configuration-driven
(`Session:*` — no magic numbers). A missing, expired, or forged cookie is treated as a fresh empty
session, never an error.

Isolation is **enforced architecturally, not by hand in handlers**:

- Every session-owned entity implements **`ISessionOwned`** (a non-nullable `UserSessionId`, indexed).
- `RagBookDbContext` applies a **global query filter** to *every* `ISessionOwned` entity type:
  `e => e.UserSessionId == sessionContext.UserSessionId`, keyed to the injected `ISessionContext`
  (resolved once per request by `SessionMiddleware`). A handler that forgets to filter **still**
  cannot read another session's rows.
- `SessionStampingInterceptor` stamps `UserSessionId` on insert centrally, so handlers never set it.
- Because a cross-session read returns nothing, requesting another session's resource by id resolves
  to **404 Not Found — never 403** — so resource existence is never disclosed.

This is verified by the `tests/RagBook.Api.IntegrationTests` suite (Testcontainers PostgreSQL) for
AC-1..AC-4, and by an offline model test asserting the query filter is present on every
`ISessionOwned` entity.

## Limit plików (file quota)

Each session gets a **free-tier file quota**, enforced **server-side before any write** (US-05):

| Limit | Default | Config key |
|---|---|---|
| Documents per session | **10** | `Quota:MaxDocuments` |
| Single file size | **10 MB** | `Quota:MaxFileSizeMb` |
| Total storage per session | **50 MB** | `Quota:MaxTotalMb` |

Every limit is **config-driven — no magic numbers**. The defaults model the free tier; **"quota-ready"**
means raising a tier is a **configuration edit only** (`QuotaOptions` bound from the `Quota` section),
no code change. MB are decimal (1 MB = 1,000,000 bytes).

- The **`Documents` module** owns the quota slice: `IQuotaService` decides admission against the pure
  `QuotaSnapshot`, reading the session's usage through the `IDocumentQuotaRepository` seam. `GET /api/quota`
  returns the current state (used/limits, `canUpload`) for the UI counter.
- **Failed** documents count toward the quota; **demo** documents (`DocumentOrigin.Demo`, US-03) do not —
  a forward-looking seam, not built here. The real upload (US-04) admits files through the same
  `TryAdmitAsync` seam.
- Breaches return a stable `quota.*` code (`quota.exceeded`, `quota.total_size_exceeded`,
  `quota.file_too_large`) through the `Result<T>` → RFC 9457 ProblemDetails channel — never a naked 500.
- **Concurrency (AC-5):** the quota-check-and-insert is **atomic** — a **transaction-scoped PostgreSQL
  advisory lock** (`pg_advisory_xact_lock`) keyed by session id serializes admissions, and usage is
  re-read *under the lock*. Two concurrent uploads at 9/10 admit **at most one** — proven by a
  Testcontainers PostgreSQL integration test.
- **Frontend:** a signals-based `QuotaStore` backs the `app-quota-bar` component ("X / 10 plików",
  "X / 50 MB"); it refreshes from `GET /api/quota` after any upload or deletion so the counter updates
  without a page reload.

## Hierarchia folderów (materialized path)

Folders organise a session's documents into a tree the visitor can **create, rename, and delete**,
nested up to **3 levels** (US-09). The hierarchy uses a **materialized path whose segments are folder
ids**, not names:

| Folder | `path` |
|---|---|
| `Umowy` (root, id `A`) | `/A/` |
| `2026` (child, id `B`) | `/A/B/` |
| `Q1` (grandchild, id `C`) | `/A/B/C/` |

- **Subtree = prefix match, no recursive CTEs.** A folder's descendants are `WHERE path LIKE parent.path
  || '%'`, backed by a `text_pattern_ops` index on `path`. Depth is the **segment count**, so the
  3-level limit is a segment check on the parent (`FolderErrors.MaxDepthExceeded`).
- **Rename is O(1).** Because segments are ids (not names), changing a folder's name never rewrites any
  path — descendants are untouched.
- **Names are unique per parent, case-insensitively.** Enforced by **two partial unique indexes** on
  `(user_session_id, [parent_id,] LOWER(name))` — one `WHERE parent_id IS NULL` (root), one `WHERE
  parent_id IS NOT NULL` (nested), because Postgres treats `NULL` parent_ids as distinct. A race is
  caught by the constraint and mapped to `folder.duplicate_name`; names are trimmed before validation.
- **Only empty folders delete.** A self-referencing `parent_id` FK with `ON DELETE RESTRICT` refuses to
  drop a folder that still has children; the "contains files" arm is the forward-looking
  `IFolderFileProbe` seam — **US-04 replaced its no-op with a real `documents.folder_id` query**, so a
  folder holding files can no longer be deleted. Non-empty → `folder.not_empty`.
- Every limit is **config-driven** (`Folders:MaxDepth` = 3, `Folders:MaxNameLength` = 100); breaches
  return stable `folder.*` codes through the `Result<T>` → ProblemDetails channel. **Frontend:** a
  signals `FolderTreeStore` backs the `app-folder-tree` component (create/rename/delete context actions;
  "New folder" is hidden at max depth).

## Upload dokumentu (US-04)

Visitors upload **PDF/TXT/Markdown** files into a folder (or the root) via `POST /api/documents`
(multipart). Validation is **by content, not extension**:

- A PDF is identified by its `%PDF-` signature; any other upload must be **valid UTF-8 text** (rejecting
  binaries renamed `.txt`) and is classified `.md` → `text/markdown`, else `text/plain`. Mismatches →
  `document.unsupported_file_type`; 0-byte → `document.empty_file`.
- The order is **empty → type → size → folder → store → atomic quota admit**. Size, count, and total
  limits are the **US-05 quota** (`Quota:*`, config-driven); the count/total admit reuses the US-05
  **advisory lock**, so a folder-target upload and the quota stay atomic under concurrency.
- Binaries live **outside Postgres** behind **`IFileStorage`** (a local volume in dev via
  `FileStorage:RootPath`; cloud object storage in prod). **Store-then-record with cleanup**: the row is
  written only after the blob is stored; a failed admit/insert deletes the blob (no orphans).
- A duplicate name in a folder is **auto-suffixed** `name (n).ext` (n from 1), computed under the
  session lock and backed by two partial unique indexes on `(folder_id, LOWER(file_name))` — never
  blocking, never overwriting, race-safe.
- On success the document is recorded `Processing` and a **`DocumentUploaded`** event is published for
  background processing (US-06). **Frontend:** a signals `DocumentUploadStore` + `app-document-upload`
  (button + drag-and-drop + progress + client pre-validation) refreshes the tree and quota without a reload.

### Known limitations

- Orphaned data from expired/deleted sessions is **not** garbage-collected (out of scope for the MVP;
  no GDPR-style cleanup yet).
- **Cascade folder delete** is not built — only empty folders can be deleted (US-09); deleting a folder
  with its contents is future work.
- A forged cookie simply starts an empty session; there is no session recovery or authentication.
