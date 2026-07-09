# Tasks: File Quota (Limit plików)

**Input**: Design documents from `specs/002-us05-quota/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/quota-api.md, quickstart.md

**Tests**: Included — the constitution mandates Test-First (Red→Green→Refactor); every behavior lands
via a failing test first, at the cheapest tier that proves it (Domain → Application → Integration).

**Organization**: US-05 builds on the existing US-01 foundation, so Setup/Foundational are thin. Tasks
are grouped by the spec's user stories: US1 = see quota (AC-1), US2 = blocked on count (AC-2), US3 =
blocked on size (AC-3), US4 = frees after delete (AC-4), US5 = concurrency (AC-5). US2/US3 boundaries
are pure domain logic shared by all enforcement paths, so they land in Foundational and are asserted
per story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel (different files, no incomplete dependency).
- Paths follow plan.md (`src/RagBook/Modules/Documents`, `src/RagBook.API`, `src/RagBook.Infrastructure`, `src/Web`, `tests/…`).

---

## Phase 1: Setup (Documents module skeleton + config)

**Purpose**: Create the new module shell and wire config — no behavior yet.

- [ ] T001 Create the `Documents` module folder skeleton under `src/RagBook/Modules/Documents/` (`Domain/`, `Errors/`, `Quota/`, `Features/GetQuota/`), copying the `Session` module's shape.
- [ ] T002 [P] Add a `Quota` section to `src/RagBook.API/appsettings.json` (`MaxDocuments: 10`, `MaxFileSizeMb: 10`, `MaxTotalMb: 50`) — the only source of the limits (no magic numbers).
- [ ] T003 [P] Verify `Microsoft.Extensions.Options` is resolvable from the Core project (transitively or add to `Directory.Packages.props` + `RagBook.csproj`) so `QuotaOptions`/`IOptions<T>` compile in Core.

**Checkpoint**: Solution still builds; module folders and config exist.

---

## Phase 2: Foundational (domain + seams + persistence — BLOCKS all stories)

**Purpose**: The `Document` aggregate, the quota arithmetic, the error catalog, the count/size + atomic
seams, and the migration. Every AC depends on these.

### Domain (Red → Green)

- [ ] T004 [P] Domain test (Red): `Should_CreateProcessingUserDocument_When_CreatedForQuota` and `Should_RejectNegativeSize_When_Created` in `tests/RagBook.Domain.Tests/Documents/DocumentTests.cs`.
- [ ] T005 [P] Domain test (Red): `QuotaSnapshot` boundaries — `Should_ReturnQuotaExceeded_When_DocumentCountAtLimit` (AC-2), `Should_ReturnTotalSizeExceeded_When_AddingFileWouldCrossTotal` (AC-3), `Should_Admit_When_UsageExactlyAtLimitBoundary`, `Should_ReturnFileTooLarge_When_FileExceedsPerFileMax` — in `tests/RagBook.Domain.Tests/Documents/QuotaSnapshotTests.cs`.
- [ ] T006 [P] [US2][US3] Implement `DocumentStatus` (`Processing/Ready/Failed`) and `DocumentOrigin` (`User/Demo`) enums in `src/RagBook/Modules/Documents/Domain/`.
- [ ] T007 Implement `Document` aggregate (`ISessionOwned` + `IAuditable`; `Id`, `SizeBytes`, `Status`, `Origin`; `CreateForQuota` factory, `Processing` default, size ≥ 0) in `src/RagBook/Modules/Documents/Domain/Document.cs` (Green for T004).
- [ ] T008 [US2][US3] Implement `QuotaLimits` (bytes) and `QuotaSnapshot` (`CanAdmit`, `UsedMb`, `RemainingBytes`, `IsFull`, `CanUpload`) value objects in `src/RagBook/Modules/Documents/Domain/` (Green for T005).

### Errors & config (Green)

- [ ] T009 [P] Implement `QuotaErrors` catalog (`quota.exceeded` Conflict, `quota.total_size_exceeded` Conflict — message conveys remaining space, `quota.file_too_large` Validation, `quota.conflict` Conflict) in `src/RagBook/Modules/Documents/Errors/QuotaErrors.cs`.
- [ ] T010 [P] Implement `DocumentsExceptionHandler.TryMap` (persistence `UniqueViolation`/`ConcurrencyConflict` → `quota.conflict`; else fall through) in `src/RagBook/Modules/Documents/Errors/DocumentsExceptionHandler.cs`.
- [ ] T011 [P] Implement `QuotaOptions` (`MaxDocuments=10`, `MaxFileSizeMb=10`, `MaxTotalMb=50`, `SectionName="Quota"`, `ToLimits()` → `QuotaLimits` in bytes via decimal MB) in `src/RagBook/Modules/Documents/Quota/QuotaOptions.cs`; bind it in `src/RagBook.API/Program.cs` (`Configure<QuotaOptions>(...)`).

### Seams (abstractions) & service

- [ ] T012 Define `IDocumentQuotaRepository` (`CountAsync`, `SumSizeBytesAsync` — both excl. `Demo`; `TryAddWithinQuotaAsync(Document, QuotaLimits, ct)`) and `IQuotaService` (`GetStateAsync`, `CheckCanUpload`, `TryAdmitAsync`) in `src/RagBook/Modules/Documents/Domain/`.
- [ ] T013 [P] Application test (Red): `QuotaService` — `Should_ReturnState_When_GettingQuota` and `Should_FailFast_When_CheckCanUploadExceedsQuota` (mocked `IDocumentQuotaRepository` + `IOptions<QuotaOptions>`, factory-method SUT) in `tests/RagBook.Application.Tests/Documents/QuotaServiceTests.cs`.
- [ ] T014 Implement `QuotaService` (reads `IOptions<QuotaOptions>` + repo; builds `QuotaSnapshot`; `TryAdmitAsync` creates the `Document` via factory and delegates to `TryAddWithinQuotaAsync`) in `src/RagBook/Modules/Documents/Quota/QuotaService.cs` (Green for T013); register `AddScoped<IQuotaService, QuotaService>()` in `src/RagBook/DependencyInjection.cs`.

### Persistence

- [ ] T015 Add `DbSet<Document>` to `RagBookDbContext` and `DocumentConfiguration` (table `documents`, `size_bytes bigint`, `status`/`origin` int, `ix_documents_user_session_id`) in `src/RagBook.Infrastructure/SharedContext/Persistence/`.
- [ ] T016 Implement `DocumentQuotaRepository` — `CountAsync`/`SumSizeBytesAsync` filtering `Origin != Demo` (session scoping inherited from the global query filter); `TryAddWithinQuotaAsync` opens a transaction, takes `pg_advisory_xact_lock(<session-key>)`, re-reads count+sum, evaluates `QuotaSnapshot.CanAdmit`, inserts or returns the failure, commits — in `src/RagBook.Infrastructure/SharedContext/Persistence/DocumentQuotaRepository.cs`; register `AddScoped<IDocumentQuotaRepository, DocumentQuotaRepository>()` in `src/RagBook.Infrastructure/DependencyInjection.cs`.
- [ ] T017 Create migration `AddDocuments` (`documents` table + index) in `src/RagBook.Infrastructure.Migrations`; applied via bundle/fixture, never at startup.

**Checkpoint**: Domain green; the quota can count, size, and atomically admit; schema exists.

---

## Phase 3: User Story 1 — See the current quota (AC-1) 🎯 MVP

**Goal**: `GET /api/quota` reports the session's count + usage against the limits; the quota-bar shows it.

**Independent test**: seed N docs → `GET /api/quota` returns `usedDocuments=N`, `usedMb`, limits, `canUpload`.

- [ ] T018 [P] [US1] Application test (Red): `GetQuotaQueryHandler` returns the mapped `QuotaStateResponse` (mocked `IQuotaService`) in `tests/RagBook.Application.Tests/Documents/GetQuotaQueryHandlerTests.cs`.
- [ ] T019 [US1] Implement `GetQuotaQuery : IQuery<QuotaStateResponse>`, `QuotaStateResponse`, and `GetQuotaQueryHandler` (calls `IQuotaService.GetStateAsync`) in `src/RagBook/Modules/Documents/Features/GetQuota/` (Green).
- [ ] T020 [US1] Implement `GET /api/quota` endpoint in `src/RagBook.API/Endpoints/QuotaEndpoints.cs` and map it in `Program.cs`.
- [ ] T021 [US1] Integration test (Red→Green): `Should_ReportCountAndUsage_When_SessionHasDocuments` — seed documents for a session, call `GET /api/quota`, assert count/usedMb/limits/canUpload — in `tests/RagBook.Api.IntegrationTests/Quota/QuotaEndpointTests.cs`.
- [ ] T022 [P] [US1] Angular `QuotaStore` (signals, `providedIn: 'root'`: `state`, `refresh()` → `GET /api/quota`, computed `canUpload`/`isFull`) in `src/Web/src/app/core/quota.store.ts` (+ unit test with `HttpTestingController`).
- [ ] T023 [P] [US1] Angular `quota-bar` standalone component (OnPush, signals, `@if`) rendering "X / 10 plików" and "X / N MB" with two meters using design tokens (no inline hex) in `src/Web/src/app/documents/quota-bar/quota-bar.{ts,html,scss}` (+ unit test asserting the two read-outs); render it in `app.html` and call `QuotaStore.refresh()` on init.

**Checkpoint**: AC-1 demonstrable — the counter is visible and correct. MVP.

---

## Phase 4: User Story 2 & 3 — Enforcement before write (AC-2, AC-3)

**Goal**: Admitting a file that would cross the count or the total-size limit fails with the correct
code before any write; the UI reflects the full state.

**Independent test**: at 10 docs → `CheckCanUpload`/admit → `quota.exceeded`; at 45/50 MB + 8 MB → `quota.total_size_exceeded`.

- [ ] T024 [US2][US3] Integration test (Red→Green): `Should_ExcludeDemoDocuments_When_CountingQuota` — seed `User` + `Demo` + `Failed` docs, assert only non-demo count/size are reported (FR-007) — in `tests/RagBook.Api.IntegrationTests/Quota/QuotaEndpointTests.cs`.
- [ ] T025 [US2][US3] Verify the admit path returns `quota.exceeded` (AC-2) and `quota.total_size_exceeded` with remaining-space message (AC-3) via `TryAddWithinQuotaAsync`; add an integration assertion `Should_RejectAdmit_When_CountOrTotalExceeded` (seed at limit, attempt admit, assert failure + no row inserted) in `tests/RagBook.Api.IntegrationTests/Quota/`.
- [ ] T026 [P] [US2][US3] Angular: `quota-bar` shows the "Limit osiągnięty — usuń pliki" hint when `isFull`, and `QuotaStore` exposes `canUpload`/`isFull` for the upload button's disabled+tooltip state (consumed by US-04) — extend `quota-bar` + its unit test.

**Checkpoint**: AC-2/AC-3 enforced server-side (domain boundaries from T005 + real admit from T025); demo exclusion proven.

---

## Phase 5: User Story 4 — Quota frees up after deletion (AC-4)

**Goal**: Removing a document drops the count/usage and re-enables upload without a page reload.

**Independent test**: at full quota, remove a row → `GET /api/quota` reflects the freed slot, `canUpload: true`.

- [ ] T027 [US4] Integration test: `Should_ReflectFreedSlot_When_DocumentRemoved` — fill to limit, delete a row directly (US-08 not built), re-read `GET /api/quota`, assert count/usedMb dropped and `canUpload: true` — in `tests/RagBook.Api.IntegrationTests/Quota/QuotaEndpointTests.cs`.
- [ ] T028 [US4] Confirm `QuotaStore.refresh()` is the shared hook US-04/US-08 call to update the quota-bar without reload; document the seam contract in a code comment on `QuotaStore` (the delete action itself is US-08).

**Checkpoint**: AC-4 validated against the seam; the refresh hook is ready for US-08.

---

## Phase 6: User Story 5 — Concurrent uploads at the boundary (AC-5) ⚠️ MANDATORY

**Goal**: Two concurrent admits at 9/10 admit at most one; the final count never exceeds the limit.

**Independent test**: seed 9 docs, fire two `TryAddWithinQuotaAsync` concurrently → exactly one success, count == 10.

- [ ] T029 [US5] Integration test (Red): `Should_AdmitAtMostOneDocument_When_TwoUploadsRaceAtLimit` — seed 9 `User` documents for one session against the Testcontainers DB, build two independent `DocumentQuotaRepository` instances (own `DbContext`/connection, same session), run `TryAddWithinQuotaAsync` concurrently via `Task.WhenAll`, assert exactly one `IsSuccess` and final `CountAsync() == 10` — in `tests/RagBook.Api.IntegrationTests/Quota/QuotaConcurrencyTests.cs`.
- [ ] T030 [US5] Ensure `TryAddWithinQuotaAsync` takes the `pg_advisory_xact_lock` (session-keyed bigint) as the first statement inside the transaction and re-reads counts under the lock (Green); tune until T029 is reliably green across repeated runs.
- [ ] T031 [P] [US5] Add a domain/edge assertion `Should_KeepExistingDocuments_When_LimitLoweredBelowUsage` (FR-009) — with usage above a lowered `QuotaLimits`, `CanAdmit` rejects new files but no document is mutated — in `tests/RagBook.Domain.Tests/Documents/QuotaSnapshotTests.cs`.

**Checkpoint**: AC-5 proven under real concurrency; the atomic admit is US-04's ready entry point.

---

## Phase 7: Docs & polish (cross-cutting)

- [ ] T032 Update `README.md` with the quota limits (10 / 10 MB / 50 MB), the `Quota:*` config section, the "quota-ready" rationale (config-only tier changes), the `Failed`-counts / `Demo`-excluded decision, and the advisory-lock atomicity note.
- [ ] T033 Run `fm-ensure-agents-md.sh`, then record durable knowledge in `AGENTS.md` (Documents module + quota seam; advisory-lock concurrency pattern; `Document` is minimal until US-04 extends it; MB = decimal convention).
- [ ] T034 Full green run: `dotnet test RagBook.slnx` (Domain + Application + Testcontainers Integration) and `npm test` in `src/Web`; verify `dotnet run --project src/RagBook.AppHost` starts clean and the quota-bar renders.

---

## Dependencies & execution order

- **Setup (T001–T003)** → **Foundational (T004–T017)** block every story.
- **US1 (T018–T023)** is the MVP (read + UI). **US2/US3 (T024–T026)** reuse the Foundational domain
  boundaries + admit path. **US4 (T027–T028)** depends on the read endpoint. **US5 (T029–T031)** depends
  on the atomic admit (T016).
- Within a phase, `[P]` tasks touch different files and may run in parallel. Test tasks precede their
  implementation (Red→Green→Refactor).
- Polish (T032–T034) after all stories are green.

## Parallel example (Foundational)

T004, T005, T006, T009, T010, T011 (`[P]`) touch independent files and can run together; T007/T008
(implementations) follow their Red tests; T015–T017 (persistence) follow the seam definitions.

## MVP scope

**US1 (T001–T023)** yields a demonstrable increment: the quota-bar shows "X / 10 plików" and "X / 50
MB" for the current session. US2–US5 complete enforcement, the delete-refresh seam, and the mandatory
concurrency guarantee required by the Definition of Done.
