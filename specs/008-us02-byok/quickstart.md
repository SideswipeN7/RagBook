# Quickstart — US-02 BYOK validation guide

Proves the feature end-to-end. Backend seams to Anthropic are faked in tests; a manual check with a real key is optional.

## Prerequisites

- .NET 10 SDK; Docker running (integration tier uses Testcontainers `pgvector/pgvector:pg17`).
- Node + Angular toolchain for the frontend tier.
- No migration needed (feature has no DB entity).

## Automated verification (the gate)

Run all tiers green before any PR (see repo memory: all-tests-green-before-pr):

```sh
dotnet test tests/RagBook.Domain.Tests          # ApiKeyMask
dotnet test tests/RagBook.Application.Tests      # SetApiKey/DeleteApiKey/GetApiKeyStatus handlers + client-factory guard
dotnet test tests/RagBook.Api.IntegrationTests   # endpoints, no-store, mask-only, no-leak, cross-session
# frontend:
cd src/Web && npm test                           # api-key.store + api-key-settings component
```

### What each scenario proves (maps to spec ACs)

| Scenario | Tier | Proves |
|---|---|---|
| valid key → 200 `{status:active, maskedKey}` | Integration | AC-1 happy path, FR-002 |
| rejected key → 400 `settings.invalid_api_key`, nothing stored | Application + Integration | AC-1 negative, FR-004 |
| empty/malformed key → 400 without upstream call | Application | FR-003 |
| provider unreachable → 503 `settings.validation_unavailable`, nothing stored | Application | AC-1 edge, FR-004a |
| > N attempts/window → 429 `settings.too_many_attempts`, no upstream call | Application + Integration | FR-004b |
| GET status returns mask only, never full key | Integration | AC-2, FR-008 |
| generation guard with no key → `settings.api_key_missing` | Application | AC-3, FR-009 |
| DELETE → 204; GET → `none`; guard blocks again | Application + Integration | AC-4, FR-010 |
| full-flow log + response scan has no full key | Integration | AC-5, FR-011, SC-003/005 |
| session A key invisible to session B (`none`) | Integration | FR-012 (isolation) |
| all responses carry `Cache-Control: no-store` | Integration | FR-013 |

## Manual smoke (optional, real key)

1. `dotnet run --project src/RagBook.AppHost`, open the SPA.
2. In the settings panel, paste a real `sk-ant-api03-…` key → Save → status flips to **active** with a masked value; the full key is never shown.
3. Reload settings → still masked. Open the (future) chat area indicator → unlocked.
4. Click **Delete** → status returns to **none**; the chat indicator locks again.
5. Paste an obviously wrong key → inline error ("nieprawidłowy klucz"), nothing stored.
6. (If reproducible) simulate provider outage → distinct "spróbuj ponownie" message, not "invalid".

## Non-goals in this guide

- Actual chat/generation (US-14). US-02 stops at the `api_key_missing` seam + the UI lock signal.
- Multi-instance key sharing (`IDistributedCache`) — MVP is process-local.
