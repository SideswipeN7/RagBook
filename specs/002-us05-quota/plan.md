# Implementation Plan: File Quota (Limit plików)

**Branch**: `fm/us05-quota` (spec dir `002-us05-quota`) | **Date**: 2026-07-07 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/002-us05-quota/spec.md`

## Summary

US-05 adds a per-session **file quota** on top of the US-01 foundation: a visible counter
("X / 10 plików", "X / 50 MB"), server-side enforcement before any write, and a `GET /api/quota`
read for the UI. It introduces a new **`Documents`** vertical-slice module with a **minimal
persisted `Document`** (the smallest shape the quota can count and size — `Id`, `UserSessionId`,
`SizeBytes`, `Status`, `Origin`), the **count/size seam** the quota reads, and the **atomic
admit-and-insert seam** that US-04's upload will call. Limits come from **`QuotaOptions`** bound via
`IOptions<T>` (defaults 10 docs / 10 MB per file / 50 MB total) — zero magic numbers, "quota-ready"
for future tiers.

Technical approach: the quota arithmetic is a pure domain value object (`QuotaSnapshot.CanAdmit`)
so AC-2/AC-3 boundaries are domain-tested cheaply; `IQuotaService` orchestrates reads and the
atomic admit; the atomic admit (AC-5) uses a **transaction-scoped PostgreSQL advisory lock keyed by
the session id** so two concurrent uploads at 9/10 admit at most one, proven by a Testcontainers
concurrency test. `Failed` documents count; `Demo`-origin documents are excluded via the seam
(forward-looking for US-03). The frontend adds a signals-based `QuotaStore` and a `quota-bar`
component refreshed after upload/delete.

> **Open scope decision escalated to the captain** (see spec Clarifications): US-05 builds a
> **minimal persisted `Document` + table now** because AC-5's atomic quota-check+insert is only
> provable against a real insert path. US-04 extends the same `Document`/table with
> filename/content/processing. If the captain prefers a pure abstract seam with no table, AC-5's
> concurrency proof moves to US-04 — recorded as the trade-off.

## Technical Context

**Language/Version**: C# (LangVersion `preview`) on **.NET 10**; TypeScript (Angular latest stable)

**Primary Dependencies**: ASP.NET Core, **Wolverine** (in-process dispatch), EF Core + Npgsql,
FluentValidation, `Microsoft.Extensions.Options` (config binding), .NET Aspire, Angular standalone/signals

**Storage**: PostgreSQL — a new `documents` table (id, user_session_id + index, size_bytes, status,
origin, audit columns). Advisory locks (`pg_advisory_xact_lock`) provide the AC-5 atomicity.

**Testing**: xUnit + FluentAssertions across three tiers; **Testcontainers** PostgreSQL for the
integration tier (GET /api/quota happy path, AC-5 concurrency, demo-exclusion query); Angular unit tests.

**Target Platform**: Linux container → GCP Cloud Run (stateless API); modern browsers for the SPA

**Project Type**: Web application (Angular SPA + ASP.NET Core modular monolith), Aspire-orchestrated

**Performance Goals**: Not a US-05 driver. The quota read is one indexed COUNT + SUM per request; the
atomic admit adds one per-session advisory lock (no cross-session contention).

**Constraints**: Enforcement is server-side, before any write; all limits config-driven (no magic
numbers); errors via `Result<T>` → ProblemDetails with a stable `code`, never a naked 500; isolation
inherited from US-01's global query filter (never bypassed); migrations applied out-of-band.

**Scale/Scope**: Case-study scale (single-digit concurrent users). US-05 scope is the quota mechanism,
the read endpoint, the config, the count/size + atomic-admit seams, a minimal `Document`, and the
quota-bar UI — explicitly NOT the upload flow (US-04), delete (US-08), or demo mode (US-03).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | US-05 compliance |
|---|---|
| **I. Vertical-slice modular monolith** | New `Documents` module (`Domain/`, `Errors/`, `Features/GetQuota/`, `Quota/`) alongside `Session`; no cross-module references — the quota seam is consumed by US-04 within the same module. ✅ |
| **II. CQRS + Result contract** | `GetQuotaQuery : IQuery<QuotaStateResponse>`; `IQuotaService` returns `Result`; `QuotaErrors` closed catalog (`quota.exceeded`, `quota.total_size_exceeded`, `quota.file_too_large`); `DocumentsExceptionHandler` (infra→code) reuses the global ProblemDetails mapper. Permissions/ deferred — see Complexity Tracking. ✅ (justified deviation) |
| **III. Data isolation by session** | `Document` implements `ISessionOwned`; all counts/sums flow through the existing global query filter (per-session) — the quota can never read another session's documents. ✅ |
| **IV. Test-first (Red→Green→Refactor)** | Domain (`QuotaSnapshot.CanAdmit` boundaries = AC-2/AC-3, `Document` invariants), Application (`GetQuotaQueryHandler`, `QuotaService` with mocked repo, factory-method SUT), Integration (Testcontainers: AC-1 read, AC-5 concurrency, demo exclusion). ✅ |
| **V. Provider resilience + cache** | No external providers in US-05 — N/A. ✅ |
| **VI. Auditing & time** | `Document` implements `IAuditable`; stamped by the existing `AuditingInterceptor` via `TimeProvider`; `UserSessionId` stamped by the existing `SessionStampingInterceptor` — never by hand. ✅ |
| **VII. Secrets** | No secrets. `QuotaOptions` bound from `Quota:*` — the config-driven limits §VII names explicitly, zero magic numbers. ✅ |
| **VIII. Operations & delivery** | Migration `AddDocuments` created in `RagBook.Infrastructure.Migrations`, applied via bundle/init/fixture — never at startup. AppHost/ServiceDefaults unchanged. ✅ |
| **IX. Frontend & design system** | Angular standalone `quota-bar` (OnPush, signals, new control flow) + `QuotaStore` signal; design tokens from `DESIGN.md`, no inline hex; refresh via shared store after upload/delete. ✅ |

**Gate result: PASS** with one justified deviation (Permissions/ folder deferred).

## Project Structure

### Documentation (this feature)

```text
specs/002-us05-quota/
├── plan.md              # This file
├── research.md          # Phase 0 — decisions & rationale (advisory lock, minimal Document, MB convention)
├── data-model.md        # Phase 1 — Document, QuotaSnapshot/Limits, QuotaOptions, seams
├── quickstart.md        # Phase 1 — run & validate AC-1..AC-5
├── contracts/
│   └── quota-api.md      # Phase 1 — GET /api/quota + the admit/count seam contracts
├── checklists/
│   └── requirements.md  # spec quality checklist
└── tasks.md             # Phase 2 — /speckit-tasks output
```

### Source Code (repository root) — new/changed for US-05

```text
src/
├── RagBook/                                          # Core
│   ├── DependencyInjection.cs                        # + AddScoped<IQuotaService, QuotaService>()
│   └── Modules/
│       └── Documents/                                # NEW module
│           ├── Domain/
│           │   ├── Document.cs                        # ISessionOwned + IAuditable; minimal (Id, SizeBytes, Status, Origin)
│           │   ├── DocumentStatus.cs                  # enum: Processing, Ready, Failed  (extended by US-06)
│           │   ├── DocumentOrigin.cs                  # enum: User, Demo  (Demo excluded from quota)
│           │   ├── QuotaLimits.cs                     # value object: MaxDocuments, MaxFileSizeBytes, MaxTotalBytes
│           │   ├── QuotaSnapshot.cs                   # value object: used counts + limits; CanAdmit(size)→Result, UsedMb, IsFull
│           │   ├── IQuotaService.cs                   # CheckCanUpload / GetStateAsync / TryAdmitAsync
│           │   └── IDocumentQuotaRepository.cs        # count/size seam + atomic TryAddWithinQuota
│           ├── Errors/
│           │   ├── QuotaErrors.cs                     # quota.exceeded, quota.total_size_exceeded, quota.file_too_large, quota.conflict
│           │   └── DocumentsExceptionHandler.cs       # infra exception → module code
│           ├── Quota/
│           │   ├── QuotaOptions.cs                    # bound from Quota:* (MaxDocuments/MaxFileSizeMb/MaxTotalMb)
│           │   └── QuotaService.cs                    # IQuotaService impl (reads options + repo)
│           └── Features/
│               └── GetQuota/
│                   ├── GetQuotaQuery.cs               # IQuery<QuotaStateResponse>
│                   ├── GetQuotaQueryHandler.cs
│                   └── QuotaStateResponse.cs          # read model for the UI
├── RagBook.API/
│   ├── Program.cs                                     # + Configure<QuotaOptions>(...); MapQuotaEndpoints()
│   ├── appsettings.json                               # + "Quota": { MaxDocuments, MaxFileSizeMb, MaxTotalMb }
│   └── Endpoints/QuotaEndpoints.cs                    # GET /api/quota
├── RagBook.Infrastructure/
│   ├── DependencyInjection.cs                         # + AddScoped<IDocumentQuotaRepository, DocumentQuotaRepository>()
│   └── SharedContext/Persistence/
│       ├── RagBookDbContext.cs                        # + DbSet<Document>
│       ├── Configurations/DocumentConfiguration.cs    # table map + user_session_id index
│       └── DocumentQuotaRepository.cs                 # count/sum (excl. Demo) + advisory-lock atomic admit
├── RagBook.Infrastructure.Migrations/Migrations/      # AddDocuments (documents table)
└── Web/src/app/
    ├── core/quota.store.ts                            # signals store: state + refresh() + canUpload/isFull
    └── documents/quota-bar/                           # quota-bar.ts/.html/.scss (standalone, OnPush)
tests/
├── RagBook.Domain.Tests/Documents/                    # QuotaSnapshotTests, DocumentTests
├── RagBook.Application.Tests/Documents/               # GetQuotaQueryHandlerTests, QuotaServiceTests
└── RagBook.Api.IntegrationTests/Quota/                # QuotaEndpointTests (AC-1), QuotaConcurrencyTests (AC-5), demo-exclusion
```

**Structure Decision**: `Documents` is a sibling module under `src/RagBook/Modules/`, copying the
`Session` module shape. US-05 fills only `Domain/`, `Errors/`, `Quota/`, and `Features/GetQuota/`;
US-04 later adds `Features/UploadDocument/` (and US-08 `Features/DeleteDocument/`) in the same module,
both calling this story's `IQuotaService`/`IDocumentQuotaRepository` seams.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| `Documents` module ships **no `Permissions/` folder** (§II says every module owns one) | Sessions are anonymous with **no roles**; the quota applies uniformly to every session — there is nothing to authorize. | An empty `Permissions/` folder is dead scaffolding; re-introduced by the first story with a real permission surface, copying the module template then (same decision as US-01). |
| US-05 introduces a **minimal persisted `Document` + `documents` table** that arguably belongs to US-04 | **AC-5 is mandatory** and requires an atomic quota-check+insert proven against a real table under real concurrency; a purely abstract seam cannot demonstrate at-most-one admission. The minimal shape (`SizeBytes`, `Status`, `Origin`) is exactly what the quota reads. | Deferring all persistence to US-04 would strand AC-5's concurrency proof in a later story and leave the quota with nothing real to count/size in US-05. Escalated to the captain (spec Clarifications) as the trade-off. |
| US-05 provides an **atomic `TryAdmitAsync` seam** with no upload endpoint | It is the exact seam US-04's upload command will call; building it now lets AC-5 be proven and gives US-04 a ready, tested atomic admission. | A test-only insert would not exercise the real advisory-lock transaction path US-04 depends on. |

> **Approval decision point for the captain**: (a) confirm building the minimal persisted `Document`
> + table in US-05 (vs. deferring to US-04), and (b) confirm the advisory-lock concurrency strategy
> for AC-5 (alternatives weighed in research.md D3). Plan proceeds on **build-minimal-Document +
> advisory-lock** unless told otherwise.

## Phase notes

- **Phase 0 (research.md)** — decisions: minimal `Document` shape & why; the count/size seam;
  advisory-lock atomic admit (vs. `Serializable`/unique-constraint); the MB convention (decimal MB);
  `Failed`-counts / `Demo`-excluded seam; `QuotaOptions` binding.
- **Phase 1 (data-model.md, contracts/, quickstart.md)** — `Document` + enums + `QuotaLimits` /
  `QuotaSnapshot`; `QuotaOptions`; the `GET /api/quota` contract + the admit/count seam contract; the
  runnable quickstart proving AC-1..AC-5.
- **Phase 2 (tasks.md)** — produced by `/speckit-tasks`, ordered Red→Green→Refactor per tier, AC-5 last.
