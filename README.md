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

### Known limitations

- Orphaned data from expired/deleted sessions is **not** garbage-collected (out of scope for the MVP;
  no GDPR-style cleanup yet).
- A forged cookie simply starts an empty session; there is no session recovery or authentication.
