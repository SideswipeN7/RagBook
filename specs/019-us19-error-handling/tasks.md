# Tasks: Obsługa błędów i stany brzegowe — error handling (US-19)

**Input**: Design documents from `specs/019-us19-error-handling/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/error-contract.md, quickstart.md

**Tests**: REQUIRED (constitution §IV; standing rule — all 4 tiers green before any PR).

**Organization**: Grouped by user story. Mostly consolidation — a shared backend correlation-id core + a shared
frontend dictionary in **Foundational**; each story then verifies/consolidates.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: different file, no dependency on an incomplete task → parallelizable.
- **[Story]**: US1 (one dictionary), US2 (invalid key), US3 (provider unavailable), US4 (correlation id), US5 (no raw exceptions).

---

## Phase 1: Setup

- [x] T001 Confirm branch `fm/us19-error-handling` off master (US-03 merged `4918d29`); no new packages.

---

## Phase 2: Foundational (Blocking Prerequisites)

### Backend correlation-id core

- [x] T002 [P] `CorrelationId` static helper (`Current(HttpContext) => Activity.Current?.Id ?? ctx.TraceIdentifier`) in `src/RagBook.API/ProblemDetails/CorrelationId.cs`.
- [x] T003 [P] `TraceHeaderMiddleware` (stamps `X-Trace-Id` via `Response.OnStarting`, value = `CorrelationId.Current`) in `src/RagBook.API/Middleware/TraceHeaderMiddleware.cs`; register early in `Program.cs` (before endpoints, after `UseExceptionHandler`).
- [x] T004 Point `GlobalExceptionHandler` `traceId` + its log line at `Activity.Current?.Id ?? httpContext.TraceIdentifier` (was `TraceIdentifier`), so domain + unexpected share one source. (`ProblemResults`/`BulkProblemResults` already use `Activity.Current?.Id` — leave as-is, they match.)

### Frontend shared dictionary

- [x] T005 [P] `core/error-messages.ts`: `ERROR_MESSAGES: Record<string,string>` covering **every** stable code (data-model catalog, incl. the ~10 previously unmapped) + `messageForCode(code?, fallback?)`; one neutral fallback for unknown codes only.

**Checkpoint**: shared trace-id source + shared dictionary exist; stories consolidate onto them.

---

## Phase 3: User Story 1 — One complete dictionary (P1) 🎯 MVP

**Goal**: every backend code → a dedicated Polish message via one dictionary; no duplicated per-store maps.

- [x] T006 [P] [US1] Karma `core/error-messages.spec.ts` (FAIL first): `messageForCode` returns a dedicated (non-fallback) message for **every** code in an explicit full-code list; the fallback is used only for an unknown code.
- [x] T007 [US1] Refactor `core/chat.store.ts` (drop `ERROR_MESSAGES`), `core/tree.store.ts` (`MOVE_ERROR_MESSAGES`), `core/selection.store.ts` (`BULK_ERROR_MESSAGES`), `core/document-upload.store.ts` (`ERROR_MESSAGES`), `core/api-key.store.ts` (`ERROR_MESSAGES`), `documents/tree/document-tree.ts` (`FOLDER_ERROR_MESSAGES`) → all call `messageForCode`. Keep each store's existing generic fallback text as the `fallback` arg where the surface warrants a domain-specific default.
- [x] T008 [US1] Update/keep the affected store specs (chat/tree/selection/upload/api-key/document-tree) so they still assert the right message renders via the shared dictionary.

**Checkpoint**: one dictionary; all stores consume it; completeness enforced.

---

## Phase 4: User Story 2 — Invalid key mid-session (P1)

**Goal**: a rejected key → "key rejected — check settings" message; history intact. (Exists — lock with a test.)

- [x] T009 [US2] Ensure the `settings.invalid_api_key` message (shared dictionary) reads as a rejected-key + settings-action message; Karma: a stubbed SSE `error` `{code: settings.invalid_api_key}` renders it and the thread/history is unchanged.

---

## Phase 5: User Story 3 — Provider timeout / unavailable (P1)

**Goal**: provider 5xx/timeout → "temporarily unavailable — try again" + retry re-runs the last question; 429 conveys wait. (Exists — lock with tests.)

- [x] T010 [US3] Karma: a stubbed `chat.provider_unavailable` renders the temporarily-unavailable message with a retry control; `retry` re-invokes `ask` with the last question; a `chat.provider_rate_limited` / `chat.demo_rate_limited` renders a wait message. (Reuses existing behaviour; asserts via the shared dictionary.)

---

## Phase 6: User Story 4 — Correlation id (P2)

**Goal**: `X-Trace-Id` header == body `traceId`, same id in logs; unexpected error shows a short report id.

- [x] T011 [P] [US4] Integration test: any error response (e.g. a `404`/`409` from an existing endpoint) carries an `X-Trace-Id` header equal to the body `traceId`. `tests/RagBook.Api.IntegrationTests/ErrorHandling/CorrelationIdTests.cs`.
- [x] T012 [P] [US4] Integration test: a forced `error.unexpected` (T014 throw endpoint) → the `X-Trace-Id` header value appears in the captured server log for that request (a test `ILoggerProvider`/sink).
- [x] T013 [US4] Frontend: the `error.unexpected` message includes the report id where available (e.g. the chat error surface shows the id from the response); Karma asserts the id is surfaced. (Minimal — the id is on the response body/header.)

---

## Phase 7: User Story 5 — No raw exceptions (P1)

**Goal**: a forced exception → 500 ProblemDetails, no stack trace, same shape as a domain error.

- [x] T014 [US5] Add a test-only `GET /api/_test/throw` endpoint mapped **only** under a test flag (e.g. `UseSetting("Testing:ExposeThrowEndpoint","true")` in the integration factory); it throws. `Program.cs` maps it conditionally.
- [x] T015 [P] [US5] Integration test: `GET /api/_test/throw` → `500`, body `code = error.unexpected`, **no** `stack`/exception text in the body, `X-Trace-Id` present. Same ProblemDetails shape as a domain `404`.

---

## Phase 8: User Story (edge) — Offline banner + Polish

**Goal**: global offline banner; README code table.

- [x] T016 [P] `core/connectivity.service.ts` (`isOnline` signal from `navigator.onLine` + `online`/`offline` listeners) + Karma (toggles on events).
- [x] T017 [P] `shared/offline-banner/*` component ("Brak połączenia z internetem", `role="status"`, tokens, ≥360px); mount in the app shell; Karma: shows when offline, hidden when online.
- [x] T018 [P] Add the error-code catalog **table** to `docs/features/README.md` (code → meaning → UI behaviour) with the actual `module.snake_case` codes (data-model catalog).
- [x] T019 [P] Domain test: `ErrorStatusMapper.ToStatusCode` returns a non-zero status for **every** `ErrorType` value. `tests/RagBook.Domain.Tests/...` (or Application if that tier owns the mapper).
- [x] T020 Run all 4 tiers green (Domain/Application/Integration-Testcontainers/Angular-Karma) per quickstart.md; then critical diff review before the PR.

---

## Dependencies & Execution Order

- **Setup (T001)** → **Foundational (T002–T005)** blocks the rest. T002/T003/T004 (backend) and T005 (frontend) are independent.
- **US1 (T006–T008)** depends on T005 (dictionary). **US2 (T009)** / **US3 (T010)** depend on T007 (stores use the dictionary).
- **US4 (T011–T013)** depends on T002–T004 (correlation core) and T014 (throw endpoint for T012). **US5 (T014–T015)** depends on T014.
- **Edge/Polish (T016–T019)** independent; **T020** last.

## Parallel Opportunities

- T002/T003/T005 in parallel; T004 after (touches GlobalExceptionHandler only).
- T011/T012/T015 integration tests share the ErrorHandling test area → can be one file or sequenced; T016/T017/T018/T019 fully parallel.

## Implementation Strategy

**MVP** = US1 (the one dictionary) + US4/US5 (correlation id + no-raw-exceptions), which are the real deltas; US2/US3
are regression locks over existing behaviour. Build the correlation-id core + dictionary (Foundational), consolidate
the stores (US1), add the correlation-id + forced-exception integration tests (US4/US5), then the offline banner and
the README table, then the full green run + critical review before the PR.
