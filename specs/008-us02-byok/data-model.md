# Phase 1 — Data Model: US-02 BYOK

**No persistent (database) entities.** The key lives only in the in-memory session store (constitution VII). This document describes the in-memory value, the derived status, config options, and the error catalog.

## In-memory values

### ApiKeyEntry (conceptual — stored in IMemoryCache)

| Field | Type | Notes |
|---|---|---|
| (cache key) | string | `"apikey:{UserSessionId}"` — isolation by session |
| value | string | the **full** Anthropic key; never persisted, never logged, never returned |
| absolute expiry | instant | `now(TimeProvider) + ApiKeyStoreOptions.Ttl` |

- Lifecycle: `Set` (overwrite) → present until `Remove` or TTL expiry → absent. Restart clears all (process-local).
- Never serialized to any response. The only externally visible projections are **status** and **mask**.

### ApiKeyAttemptCounter (conceptual — IMemoryCache)

| Field | Type | Notes |
|---|---|---|
| (cache key) | string | `"apikey-attempts:{UserSessionId}"` |
| value | int | attempts in the current window |
| expiry | instant | `now + ApiKeyStoreOptions.ThrottleWindow` (fixed window) |

## Derived / DTO shapes

### ApiKeyStatus (enum, Domain)

- `None` — no key stored for the session.
- `Active` — a validated key is stored.

(There is no persisted `Error` status; validation errors are transient responses to the save call, not stored state.)

### ApiKeyStatusResponse (query result → GET response)

| Field | Type | Notes |
|---|---|---|
| `status` | string | `"none"` \| `"active"` |
| `maskedKey` | string? | present only when `active`, e.g. `"sk-ant-api03-…B7fA"`; **never** the full key |

### SetApiKeyRequest (POST body)

| Field | Type | Notes |
|---|---|---|
| `apiKey` | string | full key; POST body only, over HTTPS; not logged |

### ApiKeyValidationResult (enum, Domain — validator seam output)

- `Valid` → provider accepted the key (200).
- `Rejected` → provider refused (401/403) → `settings.invalid_api_key`.
- `Unavailable` → transient (timeout/5xx/429/network) → `settings.validation_unavailable`.

## Domain functions / seams

- `ApiKeyMask.Mask(string fullKey) → string` — pure; `"{recognizable-prefix}…{last4}"`. Domain-tier unit tested.
- `IApiKeyStore` — `TryGet(session) → string?`, `Set(session, key)`, `Remove(session)`.
- `IApiKeyValidator` — `ValidateAsync(key, ct) → ApiKeyValidationResult`. External seam (fakeable).
- `IApiKeyThrottle` — `TryRegisterAttempt(session) → bool` (false = over limit).
- `IAnthropicClientFactory` — `CreateForSession() → Result<IAnthropicChatClient>`; failure = `settings.api_key_missing`. Consumed by US-14; here only the no-key failure path is exercised.

## Configuration — ApiKeyStoreOptions

`SectionName = "ApiKeyStore"` (bound like `QuotaOptions`).

| Property | Type | Default | Meaning |
|---|---|---|---|
| `Ttl` | TimeSpan | `30.00:00:00` | key lifetime (mirrors session sliding window) |
| `ThrottleMaxAttempts` | int | `5` | max save/validate attempts per window per session |
| `ThrottleWindow` | TimeSpan | `00:01:00` | throttle window |

`AnthropicOptions` (`SectionName = "Anthropic"`) — validation client config:

| Property | Type | Default | Meaning |
|---|---|---|---|
| `BaseUrl` | string | `https://api.anthropic.com` | provider base |
| `AnthropicVersion` | string | `2023-06-01` | `anthropic-version` header |
| `ValidationTimeout` | TimeSpan | `00:00:10` | per-attempt timeout for the liveness check |

## Error catalog (SettingsErrors)

| Code | ErrorType | HTTP | Trigger |
|---|---|---|---|
| `settings.invalid_api_key` | Validation | 400 | empty/malformed key, or provider rejected (Rejected) |
| `settings.validation_unavailable` | Unavailable* | 503 | provider unreachable during validation (Unavailable) |
| `settings.api_key_missing` | Unauthorized | 401 | generation attempted with no active key |
| `settings.too_many_attempts` | RateLimited* | 429 | per-session throttle exceeded |

\* `RateLimited` and `Unavailable` are **new additive `ErrorType` values** + `ErrorStatusMapper` entries (see plan Complexity Tracking).

## Invariants

- The full key value never appears in: any HTTP response body, any log entry, any exception message, any DB row (there is no DB row).
- `maskedKey` is present **iff** status is `active`.
- A save is validated **before** the key is stored; a `Rejected`/`Unavailable`/throttled save stores nothing.
- Cross-session: a key set by session A is invisible to session B (status `none` for B).
