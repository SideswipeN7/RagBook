# Implementation Plan: Konfiguracja klucza AI (BYOK)

**Branch**: `008-us02-byok` (git: `fm/us02-byok`) | **Date**: 2026-07-11 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/008-us02-byok/spec.md`

## Summary

Użytkownik zapisuje własny klucz API Anthropic; klucz trzymany jest **wyłącznie** w server-side session store (IMemoryCache, TTL = TTL sesji, kluczowany po `UserSessionId`) i nigdy w bazie. Zapis waliduje klucz **minimalnym, nietokenowym** wywołaniem u dostawcy przez wąski seam `IApiKeyValidator`; wynik walidacji rozróżnia: ważny → zapisany + status „aktywny", odrzucony → `settings.invalid_api_key`, dostawca nieosiągalny → przejściowy `settings.validation_unavailable`. Zapis jest throttlowany per sesja (`settings.too_many_attempts`). Status zwraca wyłącznie maskę (`sk-ant-api03-…XXXX`), nigdy pełnej wartości; endpointy ustawień są `Cache-Control: no-store`. Blokadę czatu realizuje seam `IAnthropicClientFactory`, który przy braku klucza zwraca `settings.api_key_missing` (konsumowane przez US-14; tu dostarczamy seam + testy, bez endpointu czatu). Brak wycieków sekretu do logów/odpowiedzi jest weryfikowany testem integracyjnym skanującym logi i treści odpowiedzi.

Nowy moduł `Settings` (3 slice'y: SetApiKey, DeleteApiKey, GetApiKeyStatus), pierwszy zewnętrzny provider (Anthropic SDK) za seamem z resilience, pierwszy cache w repo (`AddMemoryCache`), oraz frontendowy komponent ustawień + store (brak routera dziś → komponent w powłoce aplikacji).

## Technical Context

**Language/Version**: C# (net10.0, LangVersion preview) backend; TypeScript / Angular (latest stable) frontend.

**Primary Dependencies**: ASP.NET Core + Wolverine (dispatch), `Microsoft.Extensions.Caching.Memory` (session store — NEW), Anthropic .NET SDK (NEW — first external provider), `Microsoft.Extensions.Http.Resilience`/Polly for the validator seam, FluentValidation. Frontend: Angular Signals, HttpClient.

**Storage**: **None persistent for this feature** — the key lives in `IMemoryCache` only (constitution VII: BYOK never in DB). No EF entity, **no migration**. Config in `appsettings.json`.

**Testing**: xUnit + FluentAssertions + NSubstitute (Domain/Application); Testcontainers `pgvector/pgvector:pg17` (Integration, real host); Karma/ChromeHeadless (Angular). External provider is faked via `IApiKeyValidator` / `IAnthropicClientFactory` in-memory fakes — no test hits Anthropic.

**Target Platform**: Linux container (GCP Cloud Run), stateless API; Angular SPA.

**Project Type**: Web (modular-monolith .NET backend + Angular SPA).

**Performance Goals**: Interactive settings (SC-001: save→active < 30 s in one attempt, dominated by one upstream validation call). Validation call SHOULD use a near-zero-cost, non-generative endpoint.

**Constraints**: Key never persisted, never logged, never returned in full (only mask). Endpoints `no-store`. Per-session throttle on save. TTL bound to session sliding window.

**Scale/Scope**: Anonymous per-session scope; a handful of keys per session lifetime; negligible cache footprint.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Assessment |
|---|---|
| I. Vertical-slice modular monolith | ✅ New module `Modules/Settings/` with `Domain/` + `Errors/` + `Features/{SetApiKey,DeleteApiKey,GetApiKeyStatus}`. Infra impls in `SharedContext/`. Web = thin endpoints. No cross-module refs. |
| II. CQRS + Result contract | ✅ `SetApiKeyCommand`/`DeleteApiKeyCommand` (ICommand<T>), `GetApiKeyStatusQuery` (IQuery<T>); handlers return `Result<T>`; closed catalog `SettingsErrors`. No throwing for expected failures. |
| III. Data isolation by session | ✅ Cache keyed by `ISessionContext.UserSessionId`; no cross-session read possible. No DB entity → no query filter needed, but isolation proven by integration test (session A save → session B status = none). |
| IV. Test-First (Red→Green) | ✅ Each behavior lands via failing test at cheapest tier (Application for handlers/validator/store logic; Integration for endpoints, no-store headers, no-leak scan, cross-session). |
| V. External providers — resilience + cache | ✅ Anthropic reached only through `IApiKeyValidator` + `IAnthropicClientFactory` seams; real impl wrapped in timeout/retry/circuit-breaker; tests swap fakes. ⚠️ First provider seam in the repo — establishes the pattern for US-06/14. |
| VI. Auditing & time | ✅ `TimeProvider` for cache TTL + throttle windows (never `DateTime.UtcNow`). No auditable DB entity. |
| VII. Secrets | ✅ Core of the feature: key only in session store, never DB, never logged, mask-only in responses. |
| VIII. Operations & delivery | ✅ No startup migration (none needed). CI runs all tiers (existing `ci.yml`). Config-driven limits via `ApiKeyStoreOptions` (TTL, throttle) — zero magic numbers. |
| IX. Frontend & design system | ✅ Standalone + OnPush + Signals settings component; password input; design tokens (no inline hex); no `window.confirm`; error codes → PL messages via a `Record` map (existing pattern). Works ≥360px. |

**Deviations requiring justification** → see Complexity Tracking (additive `ErrorType` values for 429/503).

## Project Structure

### Documentation (this feature)

```text
specs/008-us02-byok/
├── plan.md              # This file
├── research.md          # Phase 0 — decisions (validation mechanism, SDK, seams, throttle, UI placement)
├── data-model.md        # Phase 1 — in-memory ApiKeyEntry, status projection, options, error catalog
├── quickstart.md        # Phase 1 — runnable validation scenarios
├── contracts/           # Phase 1 — settings-api.md (3 endpoints) + internal seams
│   └── settings-api.md
└── tasks.md             # Phase 2 (/speckit-tasks — NOT created here)
```

### Source Code (repository root)

```text
src/RagBook/Modules/Settings/                      # NEW module (Core)
├── Errors/SettingsErrors.cs                        # invalid_api_key, validation_unavailable, api_key_missing, too_many_attempts
├── Domain/
│   ├── IApiKeyStore.cs                             # get/set/remove full key by session (impl in Infra)
│   ├── IApiKeyValidator.cs                         # ValidateAsync(key) → Valid|Rejected|Unavailable (external seam)
│   ├── IApiKeyThrottle.cs                          # TryRegisterAttempt(session) → allowed?  (impl over IMemoryCache)
│   ├── ApiKeyMask.cs                               # pure: full key → "sk-ant-api03-…XXXX"
│   └── ApiKeyValidationResult.cs                   # enum Valid|Rejected|Unavailable
└── Features/
    ├── SetApiKey/      { SetApiKeyCommand, SetApiKeyCommandHandler, SetApiKeyCommandValidator }
    ├── DeleteApiKey/   { DeleteApiKeyCommand, DeleteApiKeyCommandHandler }
    └── GetApiKeyStatus/{ GetApiKeyStatusQuery, GetApiKeyStatusQueryHandler, ApiKeyStatusResponse }

src/RagBook/Modules/Settings/ApiKeyStoreOptions.cs  # SectionName="ApiKeyStore"; Ttl, ThrottleMaxAttempts, ThrottleWindow

src/RagBook.Infrastructure/SharedContext/
├── Settings/
│   ├── MemoryCacheApiKeyStore.cs                   # IApiKeyStore over IMemoryCache, key=session, TTL from options+TimeProvider
│   ├── MemoryCacheApiKeyThrottle.cs               # IApiKeyThrottle over IMemoryCache (sliding window per session)
│   └── AnthropicApiKeyValidator.cs                # IApiKeyValidator via Anthropic SDK + resilience (non-generative check)
└── Providers/Anthropic/
    ├── IAnthropicClientFactory.cs                 # CreateForSession() → Result<client> or api_key_missing (for US-14)
    └── AnthropicClientFactory.cs

src/RagBook.API/
├── Endpoints/SettingsEndpoints.cs                 # POST/GET/DELETE /api/settings/api-key ; no-store header
├── Endpoints/SettingsContracts.cs                 # SetApiKeyRequest, ApiKeyStatusResponse DTOs
├── ProblemDetails/ErrorStatusMapper.cs            # EXTEND: RateLimited→429, Unavailable→503
└── Program.cs                                      # MapSettingsEndpoints(); Configure<ApiKeyStoreOptions>; AddMemoryCache

src/RagBook/Shared/Results/ErrorType.cs            # EXTEND (additive): RateLimited, Unavailable
Directory.Packages.props                            # ADD Anthropic SDK + resilience package versions

src/Web/src/app/
├── core/api-key.store.ts                          # mutation+status store (Signals), code→PL messages
├── settings/api-key-settings/                     # settings component (.ts/.html/.scss) — states: none|active|error
└── app.ts / app.html                              # mount settings component in the shell (no router yet)

tests/
├── RagBook.Domain.Tests/Settings/ApiKeyMaskTests.cs
├── RagBook.Application.Tests/Settings/            # SetApiKey (valid/rejected/unavailable/throttled), DeleteApiKey, GetApiKeyStatus, client-factory api_key_missing
├── RagBook.Api.IntegrationTests/Settings/         # endpoints, no-store header, mask-only, no-leak log+body scan, cross-session isolation
└── Web/src/app/**/*.spec.ts                        # api-key.store.spec.ts, api-key-settings.spec.ts
```

**Structure Decision**: Web modular-monolith. US-02 adds the first non-Documents/Folders module (`Settings`), the first cache, and the first external-provider seam. Backend follows the existing slice/handler/endpoint/options patterns verbatim (mapped from US-05 Quota + US-09 Folders). Frontend adds a settings component to the shell — a router is deferred to when chat/pages arrive (US-14), avoiding an app-wide routing change inside a small P1 slice.

## Complexity Tracking

| Violation / addition | Why needed | Simpler alternative rejected because |
|---|---|---|
| Additive `ErrorType.RateLimited`→429 and `ErrorType.Unavailable`→503 (shared kernel) | `settings.too_many_attempts` is semantically 429; `settings.validation_unavailable` is a transient upstream 503. Accurate transport status matters for the frontend + future modules. | Reusing `Conflict`(409)/`Unexpected`(500) misreports the failure class to the client and pollutes the "unknown 500" bucket. The change is purely additive to a classification enum; the constitution explicitly grows the error catalog per module (US-19 owns the full set). |
| First external-provider seam (`IApiKeyValidator`, `IAnthropicClientFactory`) | Constitution V mandates external calls behind a narrow seam with resilience and fakeable in tests. US-02 is the first to call Anthropic. | Calling the SDK directly in the handler would make the handler untestable without network and violate principle V. |
| First `IMemoryCache` in repo | BYOK requires an in-memory, session-scoped, TTL'd store (constitution VII) — DB is explicitly forbidden for the key. | No persistent alternative is permitted; a bespoke static dictionary would reinvent cache expiry/TTL that `IMemoryCache` provides. |
