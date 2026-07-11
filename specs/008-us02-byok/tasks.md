# Tasks: Konfiguracja klucza AI (BYOK)

**Input**: Design documents from `specs/008-us02-byok/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/settings-api.md, quickstart.md

**Tests**: Included — the constitution mandates Test-First (Red→Green→Refactor). Every behavior lands via a
failing test first, at the cheapest tier that proves it (Domain → Application → Integration; Web unit).

**Organization**: Greenfield module `Settings` (first cache + first external-provider seam in the repo). One
Setup phase (packages, options, DI wiring), one Foundational phase (shared seams: errors, store, validator,
throttle, mask, endpoint group — BLOCK all stories), then the stories: US1 = save+validate (AC-1) 🎯 MVP,
US2 = no-key blocks chat (AC-3), US3 = masking (AC-2), US4 = delete (AC-4), US5 = no leaks (AC-5).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel (different files, no incomplete dependency).
- Paths follow plan.md (`src/RagBook/Modules/Settings`, `src/RagBook.Infrastructure/SharedContext`, `src/RagBook.API`, `src/Web`, `tests/…`).

---

## Phase 1: Setup (shared infrastructure)

- [X] T001 [P] Add NuGet versions to `Directory.Packages.props`: `Microsoft.Extensions.Caching.Memory` and `Microsoft.Extensions.Http.Resilience`; reference both in `src/RagBook.Infrastructure/RagBook.Infrastructure.csproj`.
- [X] T002 [P] Add `ApiKeyStoreOptions` (`src/RagBook/Modules/Settings/ApiKeyStoreOptions.cs`, `SectionName="ApiKeyStore"`; `Ttl`, `ThrottleMaxAttempts`, `ThrottleWindow` with defaults per data-model) and `AnthropicOptions` (`src/RagBook.Infrastructure/SharedContext/Providers/Anthropic/AnthropicOptions.cs`, `SectionName="Anthropic"`; `BaseUrl`, `AnthropicVersion`, `ValidationTimeout`). Add both sections to `src/RagBook.API/appsettings.json`.
- [X] T003 Wire DI in `Program.cs` + `src/RagBook.Infrastructure/DependencyInjection.cs`: `Configure<ApiKeyStoreOptions>` / `Configure<AnthropicOptions>`, `AddMemoryCache()`, and the named validation `HttpClient` with a standard resilience handler (timeout/retry/circuit-breaker) targeting `AnthropicOptions.BaseUrl`.

**Checkpoint**: Solution builds; options bind; cache + resilient HttpClient registered.

---

## Phase 2: Foundational (shared seams — BLOCK the stories)

- [X] T004 [P] Domain test (Red): `ApiKeyMaskTests` — `Should_Mask_KeepingPrefixAndLast4_When_NormalKey`, `Should_MaskFully_When_KeyTooShort` — in `tests/RagBook.Domain.Tests/Settings/ApiKeyMaskTests.cs`.
- [X] T005 Implement pure `ApiKeyMask.Mask(fullKey)` → `"{prefix}…{last4}"` in `src/RagBook/Modules/Settings/Domain/ApiKeyMask.cs` (Green for T004).
- [X] T006 [P] Extend shared kernel: add `RateLimited` and `Unavailable` to `src/RagBook/Shared/Results/ErrorType.cs`, and map them (→429, →503) in `src/RagBook.API/ProblemDetails/ErrorStatusMapper.cs`.
- [X] T006a [P] Test (Red→Green): `ErrorStatusMapperTests` — `Should_Map_RateLimited_To_429`, `Should_Map_Unavailable_To_503` (plus a guard that existing mappings are unchanged) — in `tests/RagBook.Application.Tests/Shared/ErrorStatusMapperTests.cs` (resolves analyze U1).
- [X] T007 [P] Define `SettingsErrors` catalog (`settings.invalid_api_key`→Validation, `settings.validation_unavailable`→Unavailable, `settings.api_key_missing`→Unauthorized, `settings.too_many_attempts`→RateLimited) in `src/RagBook/Modules/Settings/Errors/SettingsErrors.cs`.
- [X] T008 [P] Define Domain seams + enums: `IApiKeyStore`, `IApiKeyValidator`, `IApiKeyThrottle`, `ApiKeyValidationResult` (Valid/Rejected/Unavailable), `ApiKeyStatus` (None/Active) in `src/RagBook/Modules/Settings/Domain/`.
- [X] T009 Implement `MemoryCacheApiKeyStore : IApiKeyStore` (key `apikey:{session}`, absolute TTL via `TimeProvider` + `ApiKeyStoreOptions.Ttl`) in `src/RagBook.Infrastructure/SharedContext/Settings/MemoryCacheApiKeyStore.cs`; register `AddScoped`.
- [X] T009a [P] Integration test (Red→Green): `MemoryCacheApiKeyStoreTests` — `Should_ReturnNone_When_TtlElapsed` — set a key, advance a `FakeTimeProvider` past `ApiKeyStoreOptions.Ttl`, assert `TryGet` → none (proves AC-3 expiry → `api_key_missing` path) — in `tests/RagBook.Api.IntegrationTests/Settings/MemoryCacheApiKeyStoreTests.cs` (resolves analyze U2).
- [X] T010 Implement `MemoryCacheApiKeyThrottle : IApiKeyThrottle` (fixed-window counter `apikey-attempts:{session}`, window = `ThrottleWindow`, limit = `ThrottleMaxAttempts`) in `src/RagBook.Infrastructure/SharedContext/Settings/MemoryCacheApiKeyThrottle.cs`; register.
- [X] T011 Implement `AnthropicApiKeyValidator : IApiKeyValidator` — named `HttpClient` `GET /v1/models` with the candidate key; map `200`→Valid, `401/403`→Rejected, timeout/`5xx`/`429`/network→Unavailable — in `src/RagBook.Infrastructure/SharedContext/Settings/AnthropicApiKeyValidator.cs`; register. Add an in-memory `FakeApiKeyValidator` test double in `tests/RagBook.Api.IntegrationTests/Settings/Fakes/`.
- [X] T012 Add `SettingsEndpoints` group skeleton (`MapGroup("/api/settings/api-key")` + a `no-store` response filter) and `SettingsContracts` DTOs (`SetApiKeyRequest`, `ApiKeyStatusResponse`) in `src/RagBook.API/Endpoints/`; wire `app.MapSettingsEndpoints()` in `Program.cs` (routes filled per story).

**Checkpoint**: Shared seams compile and are registered; mask green; new error statuses map. Stories can start.

---

## Phase 3: User Story 1 — Save & validate key (Priority: P1) 🎯 MVP

**Goal**: Paste a key → validated upstream → stored + status "active"; rejected/unavailable/throttled → distinct errors, nothing stored.

**Independent test**: POST a valid key → 200 `{status:active, maskedKey}`; POST a rejected key → 400 `settings.invalid_api_key`.

- [X] T013 [P] [US1] Application tests (Red): `SetApiKeyCommandHandlerTests` — `Should_StoreAndReturnActive_When_ValidatorValid`, `Should_ReturnInvalidApiKey_And_NotStore_When_ValidatorRejected`, `Should_ReturnValidationUnavailable_And_NotStore_When_ValidatorUnavailable`, `Should_ReturnTooManyAttempts_And_NotCallValidator_When_ThrottleExceeded` (NSubstitute fakes for store/validator/throttle) — in `tests/RagBook.Application.Tests/Settings/SetApiKeyCommandHandlerTests.cs`.
- [X] T014 [P] [US1] Application test (Red): `SetApiKeyCommandValidatorTests` — empty/whitespace/malformed key → invalid **without** touching the validator seam — in `tests/RagBook.Application.Tests/Settings/SetApiKeyCommandValidatorTests.cs`.
- [X] T015 [US1] Implement `SetApiKeyCommand(string ApiKey) : ICommand<ApiKeyStatusResponse>`, `SetApiKeyCommandHandler` (order: throttle → validate → store → mask), and `SetApiKeyCommandValidator` in `src/RagBook/Modules/Settings/Features/SetApiKey/` (Green for T013/T014).
- [X] T016 [US1] Integration test (Red→Green): `Should_StoreAndReturnActive_When_ValidKey` and `Should_Return400_When_ProviderRejects` — POST via test host with `FakeApiKeyValidator` (`ConfigureTestServices`), assert body + `Cache-Control: no-store` — in `tests/RagBook.Api.IntegrationTests/Settings/SetApiKeyEndpointTests.cs`.
- [X] T017 [US1] Implement `POST /api/settings/api-key` route in `SettingsEndpoints.cs` (dispatch `SetApiKeyCommand` → 200 or `ProblemResults.Problem`).
- [X] T018 [P] [US1] Frontend `ApiKeyStore.save(key)` → `POST /api/settings/api-key` → on success set status/mask + refresh; error code → PL message `Record` (`settings.invalid_api_key`, `settings.validation_unavailable`, `settings.too_many_attempts`) in `src/Web/src/app/core/api-key.store.ts` (+ `api-key.store.spec.ts` asserting POST + state transitions with `HttpTestingController`).
- [X] T019 [US1] `ApiKeySettings` component (password input + Save, states none/active/error/validating; design tokens; no native dialog) in `src/Web/src/app/settings/api-key-settings/` (+ component spec: enter key → Save → shows active mask); mount in the app shell (`app.ts`/`app.html`).

**Checkpoint**: AC-1 demonstrable — save a valid key → active + mask; bad key → readable error, nothing stored. MVP.

---

## Phase 4: User Story 2 — No key blocks chat (Priority: P1)

**Goal**: Without an active key, generation is refused server-side (`api_key_missing`) and the UI blocks the question field.

**Independent test**: With no key, the generation seam fails `settings.api_key_missing`; `GET` status = `none` → UI locked.

- [X] T020 [P] [US2] Application tests (Red): `AnthropicClientFactoryTests` — `Should_ReturnApiKeyMissing_When_NoKey`, `Should_ReturnClient_When_KeyPresent` (fake `IApiKeyStore`) — in `tests/RagBook.Application.Tests/Settings/AnthropicClientFactoryTests.cs`.
- [X] T021 [US2] Implement `IAnthropicClientFactory.CreateForSession()` + `AnthropicClientFactory` (reads store → `Result.Failure(api_key_missing)` / `Result.Success(handle)`) in `src/RagBook.Infrastructure/SharedContext/Providers/Anthropic/`; register DI (Green for T020).
- [X] T022 [P] [US2] Application test (Red): `GetApiKeyStatusQueryHandlerTests` — `Should_ReturnNone_When_NoKey`, `Should_ReturnActiveWithMask_When_KeyPresent` — in `tests/RagBook.Application.Tests/Settings/GetApiKeyStatusQueryHandlerTests.cs`.
- [X] T023 [US2] Implement `GetApiKeyStatusQuery : IQuery<ApiKeyStatusResponse>` + handler (store → none / active+mask) in `src/RagBook/Modules/Settings/Features/GetApiKeyStatus/`; add `GET /api/settings/api-key` route (Green for T022).
- [X] T024 [P] [US2] Frontend: `ApiKeyStore.refresh()` (`GET`) + `status`/`chatLocked` computed signals; app shell shows a locked chat indicator with a link to settings when `status==='none'` — in `src/Web/src/app/core/api-key.store.ts` + shell (+ spec: none → locked + link; active → unlocked).

**Checkpoint**: AC-3 — generation seam refuses without a key; UI locks and points to settings.

---

## Phase 5: User Story 3 — Masking (Priority: P2)

**Goal**: A stored key is only ever shown/returned as a mask (prefix + last 4); the full value never leaves the server.

**Independent test**: After save, `GET` and the `POST` 200 body contain the mask and never the full key.

- [X] T025 [US3] Integration test (Red→Green): `Should_ReturnMaskOnly_Never_FullKey` — save a known key via the test host, then `GET` → body contains `…{last4}` and does **not** contain the full key; also assert the `POST` 200 body carries the mask, not the full key — in `tests/RagBook.Api.IntegrationTests/Settings/ApiKeyStatusEndpointTests.cs`.
- [X] T026 [US3] Frontend: `ApiKeySettings` renders the mask + a Delete affordance when `status==='active'` (no full key in the DOM) — component spec asserting the masked value is shown and the raw key is absent.

**Checkpoint**: AC-2 — possession confirmed by mask; secret never disclosed.

---

## Phase 6: User Story 4 — Delete key (Priority: P2)

**Goal**: Delete removes the key; status returns to `none`; generation blocks again; repeat delete is idempotent.

**Independent test**: Save → Delete → `GET` none → guard blocks; second Delete → still 204.

- [X] T027 [P] [US4] Application tests (Red): `DeleteApiKeyCommandHandlerTests` — `Should_RemoveKey_When_Present`, `Should_Succeed_When_NoKey` (idempotent) — in `tests/RagBook.Application.Tests/Settings/DeleteApiKeyCommandHandlerTests.cs`.
- [X] T028 [US4] Implement `DeleteApiKeyCommand : ICommand` + handler (`store.Remove`) in `src/RagBook/Modules/Settings/Features/DeleteApiKey/` (Green for T027).
- [X] T029 [US4] Integration test (Red→Green): `Should_Delete_Then_StatusNone_And_GuardBlocks` — save, `DELETE` → 204, `GET` → none, generation seam → `api_key_missing`; second `DELETE` → 204 — in `tests/RagBook.Api.IntegrationTests/Settings/DeleteApiKeyEndpointTests.cs`; add the `DELETE /api/settings/api-key` route.
- [X] T030 [P] [US4] Frontend: `ApiKeyStore.delete()` → `DELETE` → refresh to `none`; `ApiKeySettings` Delete button with inline confirm (no `window.confirm`) — in `api-key.store.ts` + component (+ spec: Delete → status none).

**Checkpoint**: AC-4 — user removes the key; chat locks again.

---

## Phase 7: User Story 5 — No leaks (Priority: P1)

**Goal**: The full key never appears in logs or HTTP response bodies across the whole flow; all settings responses are `no-store`.

**Independent test**: Run save→status→delete with a log-capturing provider; scan logs + all bodies — no full key; assert `no-store` on all three routes.

- [X] T031 [US5] Integration test: `Should_NeverLeakFullKey_Across_FullFlow` — install an in-memory `ILoggerProvider`, run POST→GET→DELETE with a known key, assert no captured log entry and no response body contains the full key; assert `Cache-Control: no-store` on POST/GET/DELETE — in `tests/RagBook.Api.IntegrationTests/Settings/ApiKeyNoLeakTests.cs`.
- [X] T032 [US5] Harden if T031 finds a leak: ensure validator/store/handlers log only outcome + mask (never the raw key); scrub any offending log call.

**Checkpoint**: AC-5 — secret never leaks; SC-003/SC-005 proven.

---

## Phase 8: Polish & cross-cutting

- [X] T033 [P] Integration test: `Should_IsolateKey_BetweenSessions` — session A saves a key; session B `GET` → `none` (FR-012) — in `tests/RagBook.Api.IntegrationTests/Settings/ApiKeyIsolationTests.cs`.
- [X] T034 [P] Docs: add `README.md` section "Obsługa sekretów (BYOK)" (no-persistence rationale, process-local cache single-instance trade-off + `IDistributedCache` path, DB-never, mask-only, `no-store`, throttle) — DoD of US-02 — and record durable notes in `AGENTS.md` (Settings module seams, error codes, validation via `/v1/models`, first provider-seam pattern).
- [X] T035 Full green run: `dotnet test` (Domain + Application + Testcontainers Integration) and `npm test` in `src/Web`; then the critical-analysis pass on the diff before opening the PR (repo memory: critical-analysis-before-pr), and the manual smoke from quickstart.md via `dotnet run --project src/RagBook.AppHost`.

---

## Dependencies & execution order

- **Setup (T001–T003)** → **Foundational (T004–T012)** block all stories.
- **US1 (T013–T019)** is the MVP. **US2 (T020–T024)**, **US3 (T025–T026)**, **US4 (T027–T030)**, **US5 (T031–T032)** build on the shared seams; US3/US5 assert over the US1/US2 endpoints.
- Within a story, test tasks precede their implementation; `[P]` tasks touch different files.
- Polish (T033–T035) after the stories are green.

## Parallel example (Foundational)

T004 (mask test), T006 (ErrorType/mapper), T007 (SettingsErrors), T008 (seams) are independent files and can run in parallel; T009/T010/T011 (infra impls) follow the seams; T012 (endpoint group) is independent of the impls.

## MVP scope

**US1 (T001–T019)** yields a demonstrable increment: paste a valid key → it is validated and stored, status flips to active with a mask, and a bad/unreachable/spammed key returns a distinct, readable error with nothing stored. US2–US5 add the generation guard + UI lock, mask-only disclosure, delete, and the no-leak guarantee.
