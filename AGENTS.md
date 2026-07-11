# Project agent memory — RagBook

Project-intrinsic knowledge that should travel with the code. The **constitution**
(`.specify/memory/constitution.md`) is the binding source; this file elaborates, never contradicts it.

## What this is

RagBook — a case-study RAG assistant (upload → pgvector index → streamed answers with citations).
**.NET 10** modular monolith (vertical slices, `Result<T>`, Wolverine dispatch) + **Angular** SPA,
orchestrated by **.NET Aspire**, backed by **PostgreSQL + pgvector**, targeting **GCP Cloud Run**.
Currently implemented: **US-01 (user session / data isolation)** and **US-05 (file quota)** + the
greenfield foundation. The remaining stories live in `docs/features/US-*.md`; spec-driven artifacts in
`.specify/` and `specs/001-us01-session/` + `specs/002-us05-quota/`.

## Architecture / conventions

- **Fixed project shape** (do not add top-level projects without a constitution change): `RagBook`
  (Core), `RagBook.API`, `RagBook.Infrastructure`, `RagBook.Infrastructure.Migrations`,
  `RagBook.AppHost`, `RagBook.ServiceDefaults`, `src/Web` (Angular), `tests/*`.
- **Vertical slices**: `src/RagBook/Modules/<Module>/{Domain,Errors,Features}`. One folder per feature
  (`CreateResource`, `GetResource`, …); handler named feature+role (`CreateResourceCommandHandler`).
  Cross-module calls go through events, never direct references.
- **CQRS + Result**: `ICommand`/`IQuery` markers (`Shared/Messaging`); handlers return `Result<T>` and
  **never throw for expected failures**. Each module owns a closed error catalog
  (`Errors/<Module>Errors.cs`, stable `module.code`) + a `<Module>ExceptionHandler` (infra→code via
  `IPersistenceExceptionClassifier`). A global `IExceptionHandler` writes RFC 9457 ProblemDetails with
  a `code` — no naked 500s.
- **Data isolation** (US-01): entities implement `ISessionOwned`; `RagBookDbContext` applies a global
  query filter keyed to the injected `ISessionContext`; another session's resource → **404, not 403**.
  `SessionStampingInterceptor` stamps `UserSessionId` on insert; `AuditingInterceptor` stamps
  `IAuditable` via **`TimeProvider`** (never `DateTime.UtcNow`). `SessionResource` is the reference
  session-owned slice future modules copy (kept permanently — captain decision).
- **Config-driven, no magic numbers**: cookie/session tunables in `SessionCookieOptions` (`Session:*`).
- **C# style**: primary constructors; always braces; blank line before every `return`; `var` when the
  type is obvious; sorted usings; `ValueTask` when fully async; flow `CancellationToken`; XML docs on
  public members. Solution builds with **`TreatWarningsAsErrors=true`** (Migrations project excepted —
  generated code). NuGet versions are centralized in `Directory.Packages.props`; TFM `net10.0` via
  `Directory.Build.props`.
- **Frontend**: Angular standalone, OnPush, **signals**, new control flow (`@if`/`@for`). Design tokens
  from `DESIGN.md` in `src/Web/src/styles/tokens.scss` — **never inline hex**. The 404 interceptor
  (`core/not-found.interceptor.ts`) maps 404 → "resource does not exist"; the SPA holds no isolation
  logic (backend-managed cookie).

## Commands

```sh
# Backend
dotnet build RagBook.slnx
dotnet test  tests/RagBook.Domain.Tests            # no Docker
dotnet test  tests/RagBook.Application.Tests        # no Docker
dotnet test  tests/RagBook.Api.IntegrationTests     # Testcontainers PostgreSQL — START DOCKER FIRST
dotnet test  <proj> --filter "FullyQualifiedName~<Name>"   # single test

# EF migrations (created here; applied out-of-band, NEVER at startup)
dotnet tool restore                    # restore the pinned dotnet-ef local tool (dotnet-tools.json)
dotnet ef migrations add <Name> \
  --project src/RagBook.Infrastructure.Migrations \
  --startup-project src/RagBook.Infrastructure.Migrations \
  --context RagBookDbContext           # uses RagBookDbContextFactory (design-time)

# Frontend
cd src/Web && npm install && npm test   # ng test (Karma/Jasmine, headless)
```

## Uruchom lokalnie (run locally)

```sh
cd src/Web && npm install && cd -          # install SPA deps once (prerequisite for the web resource)
dotnet run --project src/RagBook.AppHost   # Aspire: PostgreSQL (pgvector) + API + Angular dev server — Docker required
```

The Aspire dashboard prints its URL on startup. The API reads connection string `ragbookdb` (injected
by Aspire); running the API standalone requires that connection string in configuration.

## Sharp edges

- **`SessionOptions` name clash**: our cookie options are `SessionCookieOptions` — the plain
  `SessionOptions` collides with `Microsoft.AspNetCore.Builder.SessionOptions` (globally imported).
- **FluentAssertions pinned to 7.2.2** (last Apache-2.0 release); v8+ carries a commercial license that
  trips the constitution's "no license-warning dependencies" rule.
- **EF 10 API**: use `GetDeclaredQueryFilters()` (not the obsolete `GetQueryFilter()`); Testcontainers 4
  requires `new PostgreSqlBuilder("image:tag")` (parameterless ctor is obsolete).
- **Integration tests need Docker**: without a running engine the Testcontainers tier errors at
  container startup, not on assertions. The middleware and offline query-filter tests run without it.
- **Angular in AppHost via `AddExecutable`**: Aspire 13.4.6 has no compatible `AddNpmApp`
  (`Aspire.Hosting.NodeJs` and the CommunityToolkit Node hosting are stuck on the incompatible 9.x
  line), so `RagBook.AppHost` orchestrates the SPA with core `AddExecutable("web","npm","../Web",
  "run","start")`. `npm install` in `src/Web` is a prerequisite before `dotnet run`-ing the AppHost.
- **Quota atomicity (US-05, AC-5)**: quota check-and-insert must be **atomic** or two concurrent
  uploads at 9/10 both admit. The `Documents` module does it with a **transaction-scoped advisory lock**
  — `DocumentQuotaRepository.TryAddWithinQuotaAsync` runs `SELECT pg_advisory_xact_lock(<key>)` (key
  derived from the session GUID), **re-reads usage under the lock**, then inserts inside one EF
  transaction. Do not "optimize" the re-read away — it is what makes the check atomic. Limits are
  config-driven via `QuotaOptions` (`Quota:*` section); MB are decimal (1 MB = 1,000,000 bytes).
  `DocumentOrigin.User` (incl. `Failed`) counts toward quota, `DocumentOrigin.Demo` does not.
- **Folder hierarchy (US-09, `Folders` module)**: materialized path with **ids as segments**
  (`/A/B/C/`, self-inclusive, leading+trailing slash — see `FolderPath`). Depth = segment count, so the
  3-level cap is a check on the parent's path. **Rename changes only `Name`** — never the path or
  descendants (that is why ids, not names, are the segments). Per-parent name uniqueness is
  **case-insensitive** and enforced by **two partial unique indexes** on `(user_session_id, [parent_id,]
  LOWER(name))` — a separate one `WHERE parent_id IS NULL` for roots, because Postgres treats NULL
  parent_ids as distinct (a single composite constraint would miss root duplicates). These functional +
  partial indexes are raw SQL in the `AddFolders` migration (EF's fluent API can't model them). Names
  are trimmed before validation/uniqueness. Delete is **empty-only**: the self-`parent_id` FK is
  `ON DELETE RESTRICT` (DB refuses to drop a folder with children → mapped to `folder.not_empty`), and
  the "has files" arm is the `IFolderFileProbe` seam whose `NoFolderFilesProbe` no-op is replaced by
  **US-04** once `documents.folder_id` exists. Limits are config-driven via `FolderOptions`
  (`Folders:*`). `FolderTreeStore` (signals) + `app-folder-tree` back the UI.
- **Document upload (US-04, `Documents/Features/UploadDocument`)**: validate **by content, not
  extension** — `FileTypeDetector` checks the `%PDF-` signature, else requires valid UTF-8 text and
  classifies `.md`→markdown / else plain (`document.unsupported_file_type`; 0 bytes →
  `document.empty_file`). Order: empty → type → size → folder → **store** → **atomic quota admit**. Size/
  count/total limits are the **US-05 `QuotaOptions`** (no new limits). `IFileStorage`/`LocalFileStorage`
  keep blobs **outside Postgres** under config `FileStorage:RootPath`; **store-then-record with
  compensation** — the handler deletes the blob if the admit/insert fails (no orphans). Duplicate names
  auto-suffix `name (n).ext` from **1**, computed **under the session advisory lock** (which serializes a
  session's uploads) — NOT via same-transaction 23505 retry (that aborts the tx on Postgres); two partial
  unique indexes on `(folder_id, LOWER(file_name))` are a backstop. `NoFolderFilesProbe` was **replaced**
  by `DocumentFolderFileProbe` (US-09 AC-5 now live). Core publishes `DocumentUploaded` via the
  `IEventPublisher` abstraction (impl `WolverineEventPublisher` in the API host — Core never references
  Wolverine); the Documents module reads folder existence through its own `IFolderReference` seam (no
  Core→Folders reference).
- **Folder+document tree (US-07, `Tree` module)**: `GET /api/tree` returns folders + documents in **one**
  response via the single **`ITreeReader`** seam (impl `TreeReader` in Infrastructure runs **two**
  session-scoped `AsNoTracking` queries — folders `LOWER(name)`, documents `Origin != Demo` newest-first;
  no N+1). The Tree slice references **neither** the Folders nor the Documents module (its own DTOs
  `TreeFolder`/`TreeDocument`, §I). Added a nullable **`documents.failure_reason`** column here
  (forward-looking — **US-06 fills it**; US-07 only displays, generic fallback when null). **Frontend:** a
  unified **`@angular/cdk` `cdk-tree`** (`app-document-tree`) **replaced** the folders-only
  `app-folder-tree`; `TreeStore` (signals) composes the nested tree + owns expansion in `sessionStorage`;
  folder mutations reuse `FolderTreeStore` **and must call `TreeStore.refresh()`** (the tree reads from
  `/api/tree`, not `/api/folders`). Decimal size via `core/file-size.ts`. `DocumentUploadStore` now
  refreshes `TreeStore` (not `FolderTreeStore`) after an upload.
