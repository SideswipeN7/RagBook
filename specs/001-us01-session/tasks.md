# Tasks: User Session (Data Isolation) + greenfield foundation

**Input**: Design documents from `specs/001-us01-session/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/session-api.md, quickstart.md

**Tests**: Included — the constitution mandates Test-First (Red→Green→Refactor), so every behavior
lands via a failing test first, at the cheapest tier that proves it.

**Organization**: Grouped by user story (US1 session issuance, US2 persistence/refresh, US3
isolation). Because this is the first, greenfield story, Setup + Foundational phases are large; the
per-story phases are thin deltas on top of the shared foundation.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel (different files, no incomplete dependency).
- Paths follow plan.md's structure (`src/RagBook`, `src/RagBook.API`, … `tests/…`).

---

## Phase 1: Setup (solution skeleton)

**Purpose**: Create the fixed six-project solution + Angular + test projects — only what US-01 needs.

- [ ] T001 Create `RagBook.slnx`, `Directory.Build.props` (`net10.0`, `LangVersion preview`, `Nullable enable`, `TreatWarningsAsErrors`), and `Directory.Packages.props` (central versions: EF Core, Npgsql, Wolverine, FluentValidation, Aspire, xUnit, FluentAssertions, Testcontainers, Moq/NSubstitute) at repo root.
- [ ] T002 [P] Create Core project `src/RagBook/RagBook.csproj` with the `Shared/` and `Modules/Session/{Domain,Errors,Features}` folder skeleton.
- [ ] T003 [P] Create `src/RagBook.API/RagBook.API.csproj` (ASP.NET Core, references Core + Infrastructure + ServiceDefaults).
- [ ] T004 [P] Create `src/RagBook.Infrastructure/RagBook.Infrastructure.csproj` with `SharedContext/` folders (references Core).
- [ ] T005 [P] Create `src/RagBook.Infrastructure.Migrations/RagBook.Infrastructure.Migrations.csproj` (references Infrastructure; migrations only).
- [ ] T006 [P] Create `src/RagBook.ServiceDefaults/RagBook.ServiceDefaults.csproj` with `AddServiceDefaults()` (OpenTelemetry, health `/health` + `/alive`, resilience).
- [ ] T007 [P] Create `src/RagBook.AppHost/RagBook.AppHost.csproj` (.NET Aspire app host).
- [ ] T008 [P] Scaffold Angular app in `src/Web/` (standalone, OnPush default, routing) with `styles/tokens.scss` importing DESIGN.md colours as CSS variables (no inline hex).
- [ ] T009 [P] Create the three test projects `tests/RagBook.Domain.Tests`, `tests/RagBook.Application.Tests`, `tests/RagBook.Api.IntegrationTests` (xUnit + FluentAssertions; Testcontainers only in the integration project).
- [ ] T010 Add all projects to `RagBook.slnx`; confirm `dotnet build RagBook.slnx` succeeds (empty projects).

**Checkpoint**: Solution builds empty.

---

## Phase 2: Foundational (shared building blocks — BLOCKS all stories)

**Purpose**: Cross-cutting primitives every story depends on. No story work begins until done.

- [ ] T011 [P] Core `Shared/Results/`: `Error`, `ErrorType` enum, `Result`, `Result<T>` (compiler-enforced success/failure) in `src/RagBook/Shared/Results/`.
- [ ] T012 [P] Core `Shared/Messaging/`: `ICommand`, `ICommand<T>`, `IQuery<T>`, `IEvent`, `IExternalEvent` markers in `src/RagBook/Shared/Messaging/`.
- [ ] T013 [P] Core `Shared/Sessions/`: `ISessionContext { Guid UserSessionId }` and `ISessionOwned { Guid UserSessionId }` in `src/RagBook/Shared/Sessions/`.
- [ ] T014 [P] Core `Shared/Auditing/`: `IAuditable` in `src/RagBook/Shared/Auditing/`.
- [ ] T015 Bind `SessionCookieOptions` (CookieName, SlidingExpirationDays=30, Secure, SameSite) from `Session:*` config in `src/RagBook.API/Sessions/SessionCookieOptions.cs` + options validation (no magic numbers).
- [ ] T016 Implement `SessionContext` (scoped, ambient accessor fed by middleware) in `src/RagBook.Infrastructure/SharedContext/Sessions/SessionContext.cs`.
- [ ] T017 Implement `RagBookDbContext` in `src/RagBook.Infrastructure/SharedContext/Persistence/` that applies the `ISessionOwned` **global query filter** generically (reflect over `ISessionOwned` implementers) using the injected `ISessionContext`.
- [ ] T018 [P] Implement `SessionStampingInterceptor` (stamp `UserSessionId` on `Added` `ISessionOwned`) and `AuditingInterceptor` (`IAuditable` via `TimeProvider`) in `src/RagBook.Infrastructure/SharedContext/Interceptors/`.
- [ ] T019 Implement global `IExceptionHandler` → RFC 9457 ProblemDetails writer (maps `ErrorType`→status, always includes `code`; unknown → sanitized 500 `error.unexpected` + traceId) in `src/RagBook.API/ProblemDetails/GlobalExceptionHandler.cs`.
- [ ] T020 Implement a `Result`→HTTP helper (success→body, failure→ProblemDetails with `code`) for endpoints in `src/RagBook.API/ProblemDetails/`.
- [ ] T021 Implement `SessionMiddleware` (read/validate cookie via `Guid.TryParse`; mint `Guid.NewGuid()` on miss/forged; publish to `SessionContext`; write/refresh cookie with mandated flags from `SessionCookieOptions` via `TimeProvider`) in `src/RagBook.API/Sessions/SessionMiddleware.cs`.
- [ ] T022 Wire DI + pipeline in `src/RagBook.API/Program.cs` and `AddApp()`/`AddInfrastructure()`: Wolverine dispatch, FluentValidation auto-registration, `SessionMiddleware`, exception handler, ServiceDefaults; behaviors ordered Logging → Validation → Transaction.
- [ ] T023 Wire `src/RagBook.AppHost` to provision PostgreSQL (pgvector-capable image), reference the API, and orchestrate the Angular dev server via `AddExecutable("web", "npm", "../Web", "run", "start")` (Aspire 13.4.6 has no compatible `AddNpmApp`); API calls `AddServiceDefaults()`.
- [ ] T024 Create the `IntegrationTestFactory` (`WebApplicationFactory` + Testcontainers `PostgreSqlContainer`; applies migrations/schema in fixture setup; helper to issue requests with a chosen session cookie) in `tests/RagBook.Api.IntegrationTests/`.

**Checkpoint**: Foundation ready — DbContext filter, middleware, error mapping, and test harness exist.

---

## Phase 3: User Story 1 — Anonymous session on first visit (P1) 🎯 MVP

**Goal**: Any endpoint issues a GUID v4 session cookie with the mandated flags and returns
empty-session state (AC-1).

**Independent test**: request `/api/session` with no cookie → cookie set + `{isNew:true,resourceCount:0}`.

- [ ] T025 [P] [US1] Domain test (Red): `Should_GenerateVersion4Guid_When_SessionCreated` and cookie-flag rules helper test in `tests/RagBook.Domain.Tests/`.
- [ ] T026 [US1] Integration test (Red): `Should_IssueSessionCookie_When_RequestHasNoCookie` asserting `Set-Cookie` with `HttpOnly`, `Secure`, `SameSite=Strict`, ~30-day expiry, GUID v4, and body `{isNew:true,resourceCount:0}` in `tests/RagBook.Api.IntegrationTests/`.
- [ ] T027 [US1] Implement `GET /api/session` endpoint returning session state (`isNew`, `resourceCount`) in `src/RagBook.API/Endpoints/` (Green).
- [ ] T028 [US1] Refactor: keep cookie writing inline in `SessionMiddleware`; ensure flags come solely from `SessionCookieOptions` (stay green).

**Checkpoint**: New visitors get an isolated identity — MVP demonstrable.

---

## Phase 4: User Story 2 — Returning user sees data, refreshed session (P1)

**Goal**: A valid cookie is recognised, own data is returned, cookie expiry refreshed; forged/expired
cookie → new empty session (AC-2).

**Independent test**: create a resource under cookie X, replay with X → resource visible + refreshed cookie.

- [ ] T029 [P] [US2] Domain test (Red): `SessionResource` invariants (`Should_RequireName_When_Created`, identity is fresh GUID) in `tests/RagBook.Domain.Tests/`.
- [ ] T030 [US2] Implement `SessionResource` aggregate (`ISessionOwned` + `IAuditable`) and `ISessionResourceRepository` abstraction in `src/RagBook/Modules/Session/Domain/` (Green).
- [ ] T031 [P] [US2] Application test (Red): `CreateResourceCommandHandler` returns id and does not set `UserSessionId` by hand (mocked repo, factory-method SUT) in `tests/RagBook.Application.Tests/`.
- [ ] T032 [US2] Implement `CreateResourceCommand` + `CreateResourceCommandHandler` (+ FluentValidation validator for `Name`) in `src/RagBook/Modules/Session/Features/CreateResource/` and `POST /api/resources` endpoint (Green).
- [ ] T033 [US2] EF config + repository impl for `SessionResource` (index on `UserSessionId`) in `src/RagBook.Infrastructure/SharedContext/Persistence/Configurations/`; add `DbSet` to `RagBookDbContext`.
- [ ] T034 [US2] Create migration `InitialSession` in `src/RagBook.Infrastructure.Migrations` (`session_resources` + `user_session_id uuid not null` + index); applied via bundle/fixture, never at startup.
- [ ] T035 [US2] Integration test (Red→Green): `Should_ReturnOwnResourcesAndRefreshCookie_When_ReturningWithValidCookie` and `Should_StartEmptySession_When_CookieIsForgedOrExpired` in `tests/RagBook.Api.IntegrationTests/`.

**Checkpoint**: Sessions persist across visits; cookie refreshed; bad cookies degrade gracefully.

---

## Phase 5: User Story 3 — Cross-session isolation (P1)

**Goal**: Another session's resource by id → 404 (never 403); it never appears in lists; the filter is
architecturally enforced (AC-3, AC-4).

**Independent test**: create under session A, read/list under session B → 404 + absent.

- [ ] T036 [P] [US3] Application test (Red): `GetResourceQueryHandler` returns `session.resource_not_found` when repo yields null (mocked) in `tests/RagBook.Application.Tests/`.
- [ ] T037 [US3] Implement `SessionErrors` catalog (`session.resource_not_found` → `ErrorType.NotFound`) and `SessionExceptionHandler` (Postgres `23505`/FK/concurrency → session codes) in `src/RagBook/Modules/Session/Errors/`.
- [ ] T038 [US3] Implement `GetResourceQuery`/`Handler` + `ListResourcesQuery`/`Handler` in `src/RagBook/Modules/Session/Features/{GetResource,ListResources}/` and `GET /api/resources/{id}` + `GET /api/resources` endpoints (Green).
- [ ] T039 [US3] Integration test: `Should_Return404_When_RequestingAnotherSessionsResourceById` (assert 404 body `code=session.resource_not_found`, **never 403**) in `tests/RagBook.Api.IntegrationTests/`.
- [ ] T040 [US3] Integration test: `Should_NotListAnotherSessionsResources_When_Listing` in `tests/RagBook.Api.IntegrationTests/`.
- [ ] T041 [US3] Integration test (AC-4): `Should_ExcludeOtherSessionRows_When_QueryingWithoutExplicitFilter` — query the `SessionResource` set under session B with no explicit `.Where`, assert zero rows, and assert `IgnoreQueryFilters()` is required to see A's row (proves the global filter). In `tests/RagBook.Api.IntegrationTests/`.

**Checkpoint**: Isolation proven end-to-end and architecturally enforced.

---

## Phase 6: Frontend & polish (cross-cutting)

- [ ] T042 [P] Angular `notFoundInterceptor` mapping 404 → "resource does not exist" signal in `src/Web/src/app/core/not-found.interceptor.ts` (+ unit test).
- [ ] T043 [P] Angular standalone shell (`app.ts`/`app.config.ts`, OnPush, Signals, new control flow) rendering empty-session state using design tokens (no inline hex).
- [ ] T044 Document the global query filter in `README.md` under an **"Izolacja danych"** section (mechanism, 404-not-403, `ISessionOwned`, config-driven cookie, orphaned-data known limitation).
- [ ] T045 Run `fm-ensure-agents-md.sh`, then update `AGENTS.md`: correct `net8.0`→`net10.0` and fill `<...>` blanks (Konwencje, Komendy, "Uruchom lokalnie") with real values.
- [ ] T046 Full green run: `dotnet test RagBook.slnx` (Domain + Application + Testcontainers Integration) and `npm test` in `src/Web`; verify `dotnet run --project src/RagBook.AppHost` starts clean.

---

## Dependencies & execution order

- **Setup (T001–T010)** → **Foundational (T011–T024)** block everything.
- **US1 (T025–T028)** is the MVP; **US2 (T029–T035)** depends on the foundation + adds the resource;
  **US3 (T036–T041)** depends on US2's resource existing.
- Within a phase, `[P]` tasks touch different files and may run in parallel. Test tasks precede their
  implementation (Red→Green→Refactor).
- Polish (T042–T046) after US1–US3 are green.

## Parallel example (Setup)

T002–T009 (`[P]`) create independent project files simultaneously; T010 (solution assembly) waits.

## MVP scope

**US1 only** (T001–T028) yields a demonstrable increment: any visitor receives an isolated,
correctly-flagged session cookie. US2+US3 complete the persistence and isolation guarantees required
by the Definition of Done.
