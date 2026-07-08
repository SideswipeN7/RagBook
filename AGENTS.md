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
