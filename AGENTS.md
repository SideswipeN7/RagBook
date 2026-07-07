# Project agent memory — RagBook

Project-intrinsic knowledge that should travel with the code. The **constitution**
(`.specify/memory/constitution.md`) is the binding source; this file elaborates, never contradicts it.

## What this is

RagBook — a case-study RAG assistant (upload → pgvector index → streamed answers with citations).
**.NET 10** modular monolith (vertical slices, `Result<T>`, Wolverine dispatch) + **Angular** SPA,
orchestrated by **.NET Aspire**, backed by **PostgreSQL + pgvector**, targeting **GCP Cloud Run**.
Currently implemented: **US-01 (user session / data isolation)** + the greenfield foundation. The
remaining stories live in `docs/features/US-*.md`; spec-driven artifacts in `.specify/` and
`specs/001-us01-session/`.

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
dotnet ef migrations add <Name> \
  --project src/RagBook.Infrastructure.Migrations \
  --startup-project src/RagBook.Infrastructure.Migrations \
  --context RagBookDbContext           # uses RagBookDbContextFactory (design-time)

# Frontend
cd src/Web && npm install && npm test   # ng test (Karma/Jasmine, headless)
```

## Uruchom lokalnie (run locally)

```sh
dotnet run --project src/RagBook.AppHost   # Aspire: PostgreSQL (pgvector) + API — Docker required
cd src/Web && npm install && npm start     # Angular dev server; proxy.conf.json forwards /api → API
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
