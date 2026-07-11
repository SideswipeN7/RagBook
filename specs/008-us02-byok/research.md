# Phase 0 — Research & Decisions: US-02 BYOK

All decisions respect the settled cross-cutting constraints (constitution + `docs/features/README.md`): key only in session store, `Result`→ProblemDetails with stable codes, config-driven limits, external calls behind a fakeable resilient seam.

## D1 — Validation mechanism (how to prove a key is valid without token cost)

- **Decision**: Validate by calling Anthropic's **`GET /v1/models`** (list models) with the candidate key in `x-api-key`. It is authenticated but **non-generative** → zero token cost, deterministic status codes: `200` = Valid, `401/403` = Rejected, `429` = provider throttling (treat as Unavailable/transient), `5xx`/timeout/network = Unavailable. Wrapped behind `IApiKeyValidator.ValidateAsync(key, ct) → {Valid, Rejected, Unavailable}`.
- **Rationale**: Cheapest possible positive proof the key is live and authorized; clean 3-way mapping onto our error codes (`invalid_api_key` / `validation_unavailable`). A minimal `messages` call would cost tokens and muddy the "unavailable vs rejected" line (a 200 with content vs an overloaded 529).
- **Alternatives rejected**: (a) minimal `messages` request — costs tokens, over-generative for a liveness check; (b) syntax-only regex validation — cannot detect revoked/no-credit keys (fails AC-1 edge case); (c) trust-on-save, validate lazily at first chat — rejected by clarify (user chose eager distinct errors).

## D2 — Anthropic SDK vs thin HttpClient for validation

- **Decision**: Implement `AnthropicApiKeyValidator` over a **named `HttpClient`** (`AddHttpClient`) hitting `/v1/models`, with a standard resilience handler (`Microsoft.Extensions.Http.Resilience` — timeout, limited retry on 5xx/transient, circuit breaker). Keep a **separate** `IAnthropicClientFactory` seam for *generation* (US-14) that will use the official Anthropic .NET SDK; do **not** couple validation to the SDK.
- **Rationale**: Validation needs one auth-checked GET — an `HttpClient` + resilience is minimal, fully fakeable, and doesn't pin US-02 to a specific SDK package/version. The generation client (streaming/SSE) is a larger concern owned by US-14; US-02 only needs the factory *shape* + the `api_key_missing` failure path.
- **Alternatives rejected**: adding the full generation SDK now — larger surface, streaming concerns irrelevant to a liveness check, and risks version churn before US-14 actually uses it. (Package id/version for the generation SDK is deferred to US-14; the `IAnthropicClientFactory` seam is a stub returning `Result.Failure(api_key_missing)` when no key, `Result.Success(handle)` when present.)
- **Note**: The Anthropic base URL and the `anthropic-version` header live in config (`AnthropicOptions`), never hard-coded.

## D3 — Session store for the key (IMemoryCache)

- **Decision**: `MemoryCacheApiKeyStore : IApiKeyStore` over `IMemoryCache` (register `AddMemoryCache()`). Cache key = `"apikey:{UserSessionId}"`; value = the full key string. Entry uses `AbsoluteExpirationRelativeToNow = ApiKeyStoreOptions.Ttl` computed against `TimeProvider`. `Set` overwrites; `Remove` deletes; `TryGet` returns the key or none.
- **Rationale**: Constitution VII forbids DB persistence for BYOK; `IMemoryCache` gives TTL/expiry for free and is process-local (matches "restart = re-enter key"). Keying by session id gives isolation for free.
- **TTL choice**: `Ttl` defaults to the session sliding window (30 days, mirrors `SessionCookieOptions.SlidingExpirationDays`) but is independently configurable. We do **not** slide the key TTL on read in MVP (absolute-from-write) — simplest correct behavior; sliding can be revisited if needed. This realizes AC-3 (expiry mid-conversation → `api_key_missing`).
- **Scale-out caveat (documented, not solved)**: `IMemoryCache` is per-instance; on multi-instance Cloud Run a user could hit an instance without their key. MVP is documented as effectively single-instance for the key; `IDistributedCache` is the drop-in later (the `IApiKeyStore` seam makes it a one-class swap). Recorded in README trade-offs.

## D4 — Throttle (abuse protection on save)

- **Decision**: `MemoryCacheApiKeyThrottle : IApiKeyThrottle` — per-session fixed-window counter in `IMemoryCache` (key `"apikey-attempts:{session}"`, value = count, entry expiry = `ThrottleWindow`). `TryRegisterAttempt(session)` increments and returns `false` once `> ThrottleMaxAttempts` in the window. Checked in the handler **before** calling the validator, so a blocked attempt never reaches Anthropic.
- **Defaults**: `ThrottleMaxAttempts = 5`, `ThrottleWindow = 00:01:00` (config-driven, zero magic numbers).
- **Rationale**: Each save triggers a paid/rate-limited upstream call; a cheap per-session gate caps cost/abuse. Fixed window is adequate for MVP (no need for a sliding-log/token bucket).
- **Failure mapping**: exceeded → `settings.too_many_attempts` (`ErrorType.RateLimited` → 429).

## D5 — Masking

- **Decision**: `ApiKeyMask` (pure function, Domain) → `"{prefix}…{last4}"` where prefix is the recognizable `sk-ant-api03-` segment when present, else a generic `sk-…`; always exactly the last 4 characters. Keys shorter than 8 chars never reach masking (rejected as invalid at validation), but the function is defensive (mask everything if too short).
- **Rationale**: Confirms possession without disclosing the secret (AC-2, FR-008). Pure + Domain-tier → cheap unit test, no I/O.

## D6 — Error taxonomy & transport mapping

- **Decision**: `SettingsErrors` catalog:
  | Code | ErrorType | HTTP |
  |---|---|---|
  | `settings.invalid_api_key` | Validation | 400 |
  | `settings.validation_unavailable` | **Unavailable (new)** | 503 |
  | `settings.api_key_missing` | Unauthorized | 401 |
  | `settings.too_many_attempts` | **RateLimited (new)** | 429 |
- **Shared-kernel change**: extend `ErrorType` enum with `RateLimited` and `Unavailable`, and `ErrorStatusMapper` with 429/503. Purely additive (justified in plan Complexity Tracking).
- **Rationale**: Correct status class per failure. `api_key_missing` = 401 ("provide credentials to generate") reads correctly to the frontend guard; `invalid_api_key` = 400 (client supplied a bad value); transient upstream = 503; throttle = 429.
- **Alternative rejected**: collapsing transient + rejected into one 400 code — explicitly rejected by clarify.

## D7 — "Block chat" without a chat module (AC-3 / FR-009 / FR-015)

- **Decision**: The server-side guard is delivered as the `IAnthropicClientFactory.CreateForSession()` seam returning `Result.Failure(settings.api_key_missing)` when the store has no key — **unit-tested now** (Application tier). No chat endpoint is built (US-14 out of scope). The frontend realizes the *UI* block by reading `GET /api/settings/api-key` status: `none` → question field disabled + link to settings.
- **Rationale**: Fulfills the intent (generation cannot proceed without a key; UI is blocked) using the exact seam US-14 will consume, without pulling chat into US-02. Keeps the story small and independently testable.

## D8 — Frontend placement (no router today)

- **Decision**: Add a standalone `ApiKeySettings` component (OnPush, Signals) mounted **in the app shell** (`app.ts` imports), not behind a router. `ApiKeyStore` holds `status`/`maskedKey`/`error` signals and calls the 3 endpoints; on success it refreshes status. Error codes → PL messages via a `Record<string,string>` (existing `document-upload.store.ts` pattern).
- **Rationale**: No router exists; introducing `provideRouter` app-wide for one panel is disproportionate to a P1 slice. When chat/pages land (US-14), routing can be introduced and the settings component relocated to a route — it's self-contained.
- **UI states**: `none` (input + Save), `active` (mask + Delete), `error`/`validating` (inline message from code map). Password input hides characters; never renders the full key.

## D9 — No-store + no-leak

- **Decision**: All `/api/settings/api-key` responses set `Cache-Control: no-store` (endpoint filter). Logging: the key is never passed to a logger; validator/store log only outcomes (`Valid|Rejected|Unavailable`) and the mask at most. An integration test installs an in-memory log provider and asserts no captured log or response body contains the full key across the full flow.
- **Rationale**: FR-011/FR-013, AC-5, SC-003/SC-005 are hard security requirements — proven, not assumed.

## Open items deferred (not blocking)

- Exact generation SDK package/version → **US-14** (only the factory seam + `api_key_missing` path is needed now).
- `IDistributedCache` for multi-instance key sharing → post-MVP (seam ready).
- Sliding vs absolute key TTL → absolute for MVP (documented).
