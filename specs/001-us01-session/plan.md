# Implementation Plan: User Session (Data Isolation) + greenfield foundation

**Branch**: `fm/us01-session` (spec dir `001-us01-session`) | **Date**: 2026-07-07 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/001-us01-session/spec.md`

## Summary

US-01 is the first story of a greenfield RagBook build, so it lays the **minimal solution
foundation** and implements anonymous per-visitor session isolation on top of it. Every
visitor gets a `UserSessionId` (GUID v4) in an `HttpOnly`/`Secure`/`SameSite=Strict` cookie
(30-day sliding expiry). Every session-owned entity carries `UserSessionId`; a single **EF
Core global query filter** — fed by an injected `ISessionContext` — makes cross-session reads
impossible by construction, so another session's resource resolves to **404, never 403**.

Technical approach: scaffold the fixed six-project solution (Core / API / Infrastructure /
Migrations / AppHost / ServiceDefaults) plus an Angular shell and a Testcontainers integration
test project — **only what US-01 needs**, not the other 19 stories. A `Session` module slice
carries the session context, the `ISessionOwned` marker, the global query filter, and one
**reference session-owned resource** (`SessionResource`) whose Create / GetById / List slices
exist to (a) serve as the copy-me template for future modules and (b) act as the subject that
the AC-1..AC-4 integration tests exercise through the real host pipeline.

## Technical Context

**Language/Version**: C# (LangVersion `preview`) on **.NET 10**; TypeScript (Angular latest stable)

**Primary Dependencies**: ASP.NET Core, **Wolverine** (in-process dispatch), EF Core + Npgsql,
FluentValidation, .NET Aspire, Anthropic/embedding SDKs (NOT used in US-01), Angular standalone

**Storage**: PostgreSQL + pgvector (pgvector unused in US-01; the image/extension is provisioned
for later stories). Session identity is a cookie; no server-side session store needed for US-01.

**Testing**: xUnit + FluentAssertions across three tiers; **Testcontainers** PostgreSQL for the
integration tier; Angular unit tests (Vitest/Karma per Angular default)

**Target Platform**: Linux container → GCP Cloud Run (stateless API); modern browsers for the SPA

**Project Type**: Web application (Angular SPA + ASP.NET Core modular monolith), Aspire-orchestrated

**Performance Goals**: Not a US-01 driver. Session resolution adds one middleware pass + one cookie
write per request; the global filter adds one indexed predicate per query.

**Constraints**: Cookie MUST be `HttpOnly`, `Secure`, `SameSite=Strict`, GUID v4, 30-day sliding
expiry; unauthorized resource access MUST be 404 not 403; all tunables config-driven (no magic
numbers); migrations applied out-of-band, never at app startup.

**Scale/Scope**: Case-study scale (single-digit concurrent demo users). US-01 scope is the session
mechanism + foundation, explicitly excluding document/folder/conversation feature domains.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | US-01 compliance |
|---|---|
| **I. Vertical-slice modular monolith** | Fixed six-project shape scaffolded; `Session` module with `Domain/` + `Features/` (CreateResource, GetResource, ListResources). No cross-module references. ✅ |
| **II. CQRS + Result contract** | `CreateResourceCommand`/`ICommand<Guid>`, `GetResourceQuery`/`ListResourcesQuery`/`IQuery<T>`, dispatched via Wolverine; handlers return `Result<T>`; `SessionErrors` closed catalog (`session.resource_not_found`); `SessionExceptionHandler` (infra→code) + global ProblemDetails mapper (no naked 500). **Permissions/ folder deferred** — see Complexity Tracking. ✅ (with justified deviation) |
| **III. Data isolation by session** | The entire story. Cookie flags + GUID v4 + `UserSessionId` column/index on every `ISessionOwned` entity + EF global query filter from `ISessionContext` + 404-not-403. ✅ |
| **IV. Test-first (Red→Green→Refactor)** | Domain tests (cookie/GUID rules, entity invariants), Application tests (handlers with mocked repo, factory-method SUT), Integration tests (Testcontainers PG) covering AC-1..AC-4. ✅ |
| **V. Provider resilience + cache** | No external providers in US-01 — N/A. Seam conventions established for later stories only. ✅ |
| **VI. Auditing & time** | `TimeProvider` injected (no `DateTime.UtcNow`); `IAuditable` + `AuditingInterceptor` established on `SessionResource` as the foundation pattern; session stamped centrally by a `SaveChangesInterceptor`, never by hand. ✅ |
| **VII. Secrets** | No AI keys in US-01. Cookie/session tunables via `SessionCookieOptions` bound from config — no magic numbers. ✅ |
| **VIII. Operations & delivery** | AppHost (Aspire) provisions PostgreSQL and wires API + Angular; `AddServiceDefaults()`; migrations created in `.Migrations`, applied via bundle/init — never at startup. ✅ |
| **IX. Frontend & design system** | Angular standalone shell (OnPush, Signals, new control flow); HTTP interceptor maps 404 → "resource does not exist"; design tokens from `DESIGN.md`, no inline hex; no isolation logic client-side. ✅ |

**Gate result: PASS** with one justified deviation (Permissions/ folder deferred — Complexity Tracking).

## Project Structure

### Documentation (this feature)

```text
specs/001-us01-session/
├── plan.md              # This file
├── research.md          # Phase 0 — decisions & rationale
├── data-model.md        # Phase 1 — entities, filter, cookie
├── quickstart.md        # Phase 1 — run & validate AC-1..AC-4
├── contracts/
│   └── session-api.md   # Phase 1 — endpoint contracts
└── tasks.md             # Phase 2 — /speckit-tasks output
```

### Source Code (repository root)

```text
RagBook.slnx
Directory.Build.props            # net10.0, LangVersion preview, Nullable enable, TreatWarningsAsErrors
Directory.Packages.props         # central NuGet versions
src/
├── RagBook/                                   # Core (domain + application)
│   ├── DependencyInjection.cs                 # AddApp() → registers module slices
│   ├── Shared/
│   │   ├── Messaging/                         # ICommand, ICommand<T>, IQuery<T>, IEvent, IExternalEvent
│   │   ├── Results/                           # Result, Result<T>, Error, ErrorType
│   │   ├── Auditing/                          # IAuditable
│   │   └── Sessions/                          # ISessionContext, ISessionOwned
│   └── Modules/
│       └── Session/
│           ├── Domain/                        # SessionResource aggregate, ISessionResourceRepository
│           ├── Errors/                        # SessionErrors, SessionExceptionHandler
│           └── Features/
│               ├── CreateResource/            # CreateResourceCommand + Handler
│               ├── GetResource/               # GetResourceQuery + Handler
│               └── ListResources/             # ListResourcesQuery + Handler
├── RagBook.API/
│   ├── Program.cs                             # Wolverine, DI composition, pipeline
│   ├── Sessions/SessionMiddleware.cs          # read/validate/issue + write cookie → ISessionContext
│   ├── Sessions/SessionCookieOptions.cs       # cookie tunables bound from Session:*
│   ├── Endpoints/                             # /api/session, /api/resources
│   └── ProblemDetails/GlobalExceptionHandler.cs
├── RagBook.Infrastructure/
│   ├── DependencyInjection.cs                 # AddInfrastructure()
│   └── SharedContext/
│       ├── Persistence/RagBookDbContext.cs    # applies global query filter for ISessionOwned
│       ├── Persistence/Configurations/        # SessionResource EF config (index on UserSessionId)
│       ├── Sessions/SessionContext.cs         # ISessionContext impl (scoped, ambient accessor)
│       └── Interceptors/                      # SessionStampingInterceptor, AuditingInterceptor
├── RagBook.Infrastructure.Migrations/         # EF migrations only (InitialSession)
├── RagBook.AppHost/                           # Aspire: postgres(pgvector) + api + angular
├── RagBook.ServiceDefaults/                   # AddServiceDefaults()
└── Web/                                        # Angular SPA
    └── src/app/
        ├── app.config.ts / app.ts             # standalone shell, OnPush
        ├── core/not-found.interceptor.ts      # 404 → "resource does not exist"
        └── styles/tokens.scss                 # DESIGN.md tokens
tests/
├── RagBook.Domain.Tests/
├── RagBook.Application.Tests/
└── RagBook.Api.IntegrationTests/               # Testcontainers PostgreSQL (AC-1..AC-4)
```

**Structure Decision**: Web-application layout instantiated as the constitution's fixed six .NET
projects + `Web/` Angular SPA + three test projects. Only the `Session` module exists now; future
stories add sibling modules under `src/RagBook/Modules/` without touching this one.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| `Session` module ships **no `Permissions/` folder** (constitution §II says every module owns one) | US-01 has anonymous sessions and **no roles or permission checks** — there is nothing to authorize. An empty catalog would be dead scaffolding. | An empty `Permissions/` folder adds noise without behavior; the pattern is re-introduced in the first story with a permission surface (BYOK/quota), copying the module template then. |
| A **`SessionResource` reference resource** (not a real product domain) with Create/Get/List slices | AC-3/AC-4 require a real endpoint returning 404 across sessions and a real query proving the global filter; the product domains (Document/Folder/Conversation) belong to later stories and must not be built here. | A test-only entity registered only in the integration host would not exercise the real DI/middleware/DbContext wiring the AC-4 test must prove, and would give future modules no copy-me reference slice. |

> **Approval decision point for the captain**: keep `SessionResource` as a permanent foundation
> reference slice, or mark it clearly as removable once US-04 lands the first real domain? Plan
> assumes **keep-and-label** unless told otherwise.

## Phase notes

- **Phase 0 (research.md)** — records concrete decisions: cookie issuance point & attributes, GUID
  v4 generation, global-query-filter shape, how the DbContext obtains the current session, session
  stamping on insert, and the Aspire/Angular wiring choices. No open NEEDS CLARIFICATION.
- **Phase 1 (data-model.md, contracts/, quickstart.md)** — `SessionResource` + `ISessionOwned` +
  `SessionCookieOptions`; the `/api/session` and `/api/resources` contracts; and the runnable quickstart
  proving AC-1..AC-4.
- **Phase 2 (tasks.md)** — produced by `/speckit-tasks`, ordered Red→Green→Refactor per tier.
