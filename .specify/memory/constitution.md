<!--
Sync Impact Report
==================
Version change: (template) → 1.0.0
Ratification: initial adoption of the RagBook constitution.
Modified principles: none (initial authoring — all placeholders replaced).
Added sections:
  - Core Principles I–IX (Vertical-Slice Architecture; CQRS + Result contract;
    Data Isolation; Test-First; Provider Resilience; Auditing & Time; Secrets;
    Operations & Delivery; Frontend & Design System)
  - Technology Stack (fixed)
  - Development Workflow & Quality Gates
  - Governance
Removed sections: none.
Templates requiring updates:
  ✅ .specify/templates/plan-template.md — Constitution Check gate aligns (generic).
  ✅ .specify/templates/spec-template.md — no mandatory-section conflict.
  ✅ .specify/templates/tasks-template.md — testing tiers/task types align.
Follow-up TODOs: none.
-->

# RagBook Constitution

RagBook is a case-study RAG assistant over a user's own documents: upload PDF/TXT/MD →
index with pgvector → ask natural-language questions → stream answers with clickable
citations. It is a **.NET 10** modular-monolith backend paired with an **Angular** SPA.
Optimise for **clarity and demonstrability** over exhaustive feature coverage. This
constitution is the source of truth; where any other document disagrees with it, the
constitution wins.

## Core Principles

### I. Vertical-Slice Modular Monolith (NON-NEGOTIABLE)

The solution is a modular monolith organised as **vertical slices** — one folder per
feature, never horizontal technical layers. The fixed project shape is:

- **`RagBook`** — Core: domain + application. `Modules/<Module>/` → `Domain/` + `Features/`,
  plus per-module `Permissions/` and `Errors/`.
- **`RagBook.API`** — web/transport only: endpoints, DI composition, ProblemDetails mapping.
- **`RagBook.Infrastructure`** — infrastructure implementations; cross-cutting code under
  `SharedContext/` (interceptors, session context, cache, provider clients, persistence).
- **`RagBook.Infrastructure.Migrations`** — EF Core migrations ONLY.
- **`RagBook.AppHost`** — .NET Aspire orchestration.
- **`RagBook.ServiceDefaults`** — shared telemetry/health/resilience (`AddServiceDefaults()`).

Rules: one folder per feature named after the use case (`GetSingle`, `Create`, …); the
handler is named after the feature + role (`GetSingleQueryHandler`, `CreateCommandHandler`)
and sits beside its query/command. **Dependency direction:** Core depends on abstractions
only; Infrastructure implements them; the web layer is a thin composition boundary.
**A feature owns its domain, interfaces, and handlers; cross-module calls go through
published events, never direct references.** New top-level projects require a constitution
amendment.

### II. CQRS + Result Contract (NON-NEGOTIABLE)

Marker interfaces drive pipeline policies and transport routing: `ICommand` /
`ICommand<TResult>` (writes), `IQuery<TResult>` (reads), `IEvent` (in-process domain
event), `IExternalEvent` (integration event → durable outbox). Dispatch is via
**Wolverine** (in-process for commands/queries). Pipeline behaviors run in order:
Logging → Validation → Auth guard → Transaction. Validators use FluentValidation and
are auto-registered.

**The response contract for the frontend is: either the body, or an error code — never
both, never a raw stack/500.** Every endpoint resolves to exactly one of: success → DTO,
or failure → a single `Error { Code, Message, ErrorType }` whose `Code` is drawn from that
module's closed error catalog (`Errors/<Module>Errors.cs`). Handlers return an explicit
`Result<T>`; **do not throw for expected/domain failures** (not-found, validation,
conflict) — throwing is reserved for truly exceptional faults. Codes are stable,
machine-readable, namespaced per module (e.g. `document.not_found`). The full RagBook code
catalog is owned by US-19; each module contributes its slice.

Two mandated safety nets: a per-module **`<Module>ExceptionHandler`** translates
expected-but-infra-shaped exceptions (Postgres `23505` unique violation, FK violation,
`DbUpdateConcurrencyException`) back into the module's error codes; a single **global
exception-mapping middleware** (`IExceptionHandler` + ProblemDetails writer) is the last
line of defense — anything reaching it becomes an RFC 9457 ProblemDetails with a `code`,
mapping known categories to statuses (validation→400, not-found→404, conflict→409,
auth→401/403) and only truly unknown faults to a sanitized 500 with a correlation id and a
generic `code` (`error.unexpected`). The frontend ALWAYS receives a valid body or a
machine-readable code — never an unmapped 500.

Authorization is by **permission**, not role, at the call site. Each module owns
`Permissions/<Module>Permissions.cs` (namespaced catalog) and
`Permissions/<Module>RolePermissions.cs` (role → permission map); guarded commands/queries
route failures through the same Result/ProblemDetails channel (→ 403).

### III. Data Isolation by Session (NON-NEGOTIABLE)

RagBook has no login and no multi-tenant model in MVP. Isolation rests on a
`UserSessionId` (GUID v4) generated on first visit and carried in a cookie that MUST be
`HttpOnly`, `Secure`, `SameSite=Strict`, with a 30-day sliding expiry refreshed each visit.

- **Every domain entity** (Document, Folder, Conversation, …) carries a non-nullable
  `UserSessionId` column with an index; there are NO global queries without a session filter.
- The filter is **enforced architecturally**, not by hand in handlers — via an EF Core
  **global query filter** on `UserSessionId` fed from an injected `ISessionContext`. Never
  bypass the query filter without an explicit, documented reason.
- Accessing another session's resource returns **404, never 403**, so existence is not
  disclosed. A forged/expired/missing cookie is treated as a fresh empty session, not an error.
- Session plumbing lives in Infrastructure `SharedContext/`; `ISessionContext { Guid UserSessionId }`
  is injected into handlers. This mechanism MUST be covered by a Testcontainers integration
  test proving cross-session 404 isolation and that the filter is applied.

### IV. Test-First — Red → Green → Refactor (NON-NEGOTIABLE)

Work test-first, always: (1) **Red** — write the smallest failing test expressing the next
behavior; run it, watch it fail. (2) **Green** — minimum production code to pass. (3)
**Refactor** — clean production and test code while green. Never write production code
without a failing test that demands it. Use the cheapest tier that proves the change:

| Tier | Project | Docker | Proves | Dependencies |
|---|---|---|---|---|
| Domain | `tests/RagBook.Domain.Tests` | no | aggregate/value-object/pure rule | construct directly |
| Application | `tests/RagBook.Application.Tests` | no | handler/validator/behavior; DB mocked | factory-method SUT builder |
| Integration | `tests/RagBook.Api.IntegrationTests` | **yes** | real host + Dockerized PostgreSQL; mock external providers; **happy-path only** | Testcontainers DB; in-memory provider fakes |

Every test is named `Should_<expected>_When_<condition>` and its body is split into
`// Arrange` / `// Act` / `// Assert` sections separated by blank lines, one logical
behavior per test. Prefer factory methods / object mothers for Arrange. Persistence,
HTTP/contract, messaging, authorization, and concurrency behavior MUST get a
PostgreSQL-backed Testcontainers integration test — integration tests exist to compile and
execute the heavy queries (EF/pgvector/SQL) unit tests cannot, not to re-test business
branches. If `docker info` fails, start Docker before running the integration tier.

### V. External Providers — Resilience + Cache (NON-NEGOTIABLE)

Wrap **every** external call (Anthropic SDK, embedding provider) with resilience (timeout,
retry, circuit breaker) and cache responses where sound. Reach third-party SDKs only
through a narrow abstraction seam so integration tests swap in-memory fakes — **no test
hits a real external service.** Embeddings are produced by a single, centralised provider
seam; one embedding model governs the whole index. RAG parameters (`TopK`, similarity
threshold, sentinel phrase) live in configuration, never as magic numbers.

### VI. Auditing & Time

Auditable entities implement `IAuditable` (`CreatedBy`, `CreatedAt`, `ModifiedBy`,
`ModifiedAt`), stamped centrally by an EF Core `SaveChangesInterceptor` — never by hand in
handlers. Timestamps are `DateTimeOffset` in UTC via **`TimeProvider`** (never
`DateTime.UtcNow`). The actor comes from `ISessionContext`/`ICurrentUser`
(`UserSessionId`, or `"system"` for background work).

### VII. Secrets

The user's AI key (BYOK) lives **only in the session store, never in the database**.
Application keys live in a secret manager (Secret Manager on GCP; user-secrets / env locally).
Secrets are never committed. Configuration limits (`QuotaOptions`, `DemoOptions`,
`RagOptions`, `ChatOptions`) are config-driven with zero magic numbers.

### VIII. Operations & Delivery

Container-first: every deployable ships as an OCI image; the API is stateless and
horizontally scalable. Runnable services are wired through `RagBook.AppHost` and
`AddServiceDefaults()` — no ad-hoc topology or observability setup. Migrations are created
in `RagBook.Infrastructure.Migrations` and applied via `dotnet ef migrations bundle` / an
init step — **never at application startup**. CI/CD on GitHub Actions: build → test → image
→ migrate → deploy (target: GCP Cloud Run). Observability via OpenTelemetry + health checks
(`/health` readiness, `/alive` liveness) through ServiceDefaults. NuGet versions are
centrally managed in `Directory.Packages.props`; the target framework is `net10.0` globally
via `Directory.Build.props`.

### IX. Frontend & Design System

Angular (latest stable): standalone components, `ChangeDetectionStrategy.OnPush`, **Signals**
for state, the new control flow (`@if`/`@for`/`@switch`). Parent→child via
`input.required<T>()`; child→parent via `output<T>()`. Split components into `.ts`/`.html`/`.scss`
(inline only for trivial leaf primitives). The design system in `DESIGN.md` is canonical:
**use design tokens, never inline hex.** Dark mode via design tokens / `:host-context`, never
leaking `html.dark .x` across components. Shared UI (cards, modals, toasts) lives in a shared
library — never `window.confirm`/`alert`/native dialogs. Every page works at ≥360px. The
frontend contains no isolation logic: the session cookie is backend-managed, and an Angular
HTTP interceptor maps 404 to "resource does not exist".

## Technology Stack (fixed)

| Concern | Choice |
|---|---|
| Runtime / language | .NET 10, C# preview LangVersion |
| Web API | ASP.NET Core; dispatch via **Wolverine** |
| Orchestration | .NET Aspire app host (service discovery, OpenTelemetry, dashboard) |
| Data access | EF Core + Npgsql |
| Datastore | **PostgreSQL + pgvector** (durable outbox for `IExternalEvent`) |
| AI | Anthropic .NET SDK (streaming/SSE); centralised embedding provider |
| Frontend | Angular (standalone, Signals, OnPush, new control flow) |
| Delivery | Docker → GCP Cloud Run; GitHub Actions CI/CD |

## Development Workflow & Quality Gates

- **Spec-driven:** feature specs live in `specs/<NNN-short-name>/` (`spec.md`, `plan.md`,
  `tasks.md`, plus `research.md`/`data-model.md`/`contracts/`). Lifecycle: specify → clarify →
  plan → tasks → analyze → implement via the `speckit-*` skills. This constitution is the
  source every spec/plan/task is validated against.
- **Cross-cutting decisions in `docs/features/README.md` "Decyzje przekrojowe" are settled
  constraints** for planning (session isolation, `Result<T>`→ProblemDetails, materialized-path
  hierarchy, single embedding model, BYOK-not-in-DB, config-driven limits) — reflected in plans,
  never re-opened.
- **C# style:** primary constructors where they cut boilerplate; always braces; blank line
  before every `return`; no trailing whitespace; `var` when the type is obvious; parameter
  objects/named args past 3 parameters; sorted grouped usings (System first); no unused usings;
  `ValueTask` when fully async with no sync pre-work; no `ConfigureAwait(false)`; flow
  `CancellationToken` downstream; `Activity`/`ActivitySource` for tracing; XML docs on public
  members; strict compiler-diagnostic compliance.
- **Verify by running `RagBook.AppHost`;** fix startup/runtime errors immediately, consult the
  maintainer before addressing warnings.

## Governance

This constitution supersedes other practices; when it and any doc (including `CLAUDE.md`/
`AGENTS.md`) disagree, the constitution wins. Amendments require a documented change to this
file and any dependent templates in the same change. Versioning is semantic: MAJOR for
backward-incompatible governance/principle removals or redefinitions, MINOR for a new or
materially expanded principle/section, PATCH for clarifications. Every plan/PR must verify
compliance with the principles above; complexity that violates a principle must be justified
in the plan's Complexity Tracking or the change is rejected. Use `AGENTS.md` for day-to-day
runtime development guidance that elaborates — but never contradicts — this constitution.

**Version**: 1.0.0 | **Ratified**: 2026-07-07 | **Last Amended**: 2026-07-07
