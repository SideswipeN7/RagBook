# RagBook

A case-study RAG assistant over your own documents: upload PDF/TXT/MD → index with pgvector →
ask natural-language questions → stream answers with clickable citations. A **.NET 10** modular
monolith (vertical slices, `Result<T>`, Wolverine) paired with an **Angular** SPA, orchestrated by
**.NET Aspire**, backed by **PostgreSQL + pgvector**, deployed to **GCP Cloud Run**.

This repository currently implements **US-01 — user session (data isolation)** plus the greenfield
solution foundation. See `docs/features/` for the full story map and `.specify/` for the
spec-driven artifacts (constitution, spec, plan, tasks).

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

### Known limitations

- Orphaned data from expired/deleted sessions is **not** garbage-collected (out of scope for the MVP;
  no GDPR-style cleanup yet).
- A forged cookie simply starts an empty session; there is no session recovery or authentication.
