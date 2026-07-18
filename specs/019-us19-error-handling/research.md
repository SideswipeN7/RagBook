# Phase 0 Research — US-19 Obsługa błędów (error handling)

## D1 — Frontend consolidation: one shared dictionary (clarify Q1)

**Decision**: A single `core/error-messages.ts` exports `ERROR_MESSAGES: Record<string, string>` (every stable
backend code → its Polish message) and `messageForCode(code: string | undefined, fallback?: string): string`. The
six existing maps (`chat.store` `ERROR_MESSAGES`, `tree.store` `MOVE_ERROR_MESSAGES`, `selection.store`
`BULK_ERROR_MESSAGES`, `document-tree` `FOLDER_ERROR_MESSAGES`, `document-upload.store` `ERROR_MESSAGES`,
`api-key.store` `ERROR_MESSAGES`) are removed; each store calls `messageForCode`. One generic fallback is used
**only** for codes absent from the dictionary. `ChatStore` keeps its raw-`fetch` path but resolves via
`messageForCode`.

**Rationale**: Lowest-risk way to satisfy AC-1/FR-001–003 — one source of truth, one wording per code, a
completeness test. A central HTTP interceptor was rejected (bigger surface; `ChatStore`'s raw `fetch` bypasses
interceptors anyway; out of scope).

**Alternatives rejected**: a central ProblemDetails interceptor + notifier (more refactor, incomplete because of
`fetch`); keeping per-store maps (the AC-1 gap itself).

## D2 — Correlation id: Activity trace id + `X-Trace-Id` header (clarify Q2)

**Decision**: A `CorrelationId.Current(HttpContext)` helper returns `Activity.Current?.Id ?? httpContext
.TraceIdentifier`. `ProblemResults` and `BulkProblemResults` set `traceId` from `Activity.Current?.Id` (their
current value — unchanged); `GlobalExceptionHandler` switches its `traceId` **and** its log line from
`TraceIdentifier` to `Activity.Current?.Id ?? TraceIdentifier`, so domain and unexpected failures share one source.
A `TraceHeaderMiddleware` (registered early) stamps `X-Trace-Id` on the response via `Response.OnStarting` using the
same expression, so **every** response carries the header and it equals the body `traceId`.

**Rationale**: `Activity.Current` is populated for every request (ASP.NET Core diagnostics / OTel), so the header
and body match, and OTel (`IncludeScopes`, `AddAspNetCoreInstrumentation`) already tags logs with the trace id — the
same id is thus in the response and the logs (AC-4/FR-006–007) with no bespoke GUID or logger-scope machinery.

**Alternatives rejected**: a generated per-request GUID + `X-Correlation-Id` + a logger scope (a second id alongside
the trace id; reinvents what OTel provides).

## D3 — `ErrorStatusMapper` completeness + the code catalog

**Decision**: Add a Domain/Application test asserting `ErrorStatusMapper.ToStatusCode` maps **every** `ErrorType`
enum value to a non-`0` status (guards a future enum addition). The full list of stable codes for the frontend
completeness test is maintained as an explicit array in `error-messages.spec.ts` (the codes are the wire contract);
the README table is the human-facing catalog. No backend code is renamed (`module.snake_case`, `error.unexpected`
kept).

**Rationale**: The codes live in closed per-module `*Errors` classes already; US-19 documents and cross-checks them
rather than centralising them (which would break §I module ownership). The frontend completeness test is the
enforcement point for FR-003.

## D4 — Forced-exception test surface (AC-5)

**Decision**: Prove AC-5 with a test-only endpoint (mapped only under the integration host / a config flag) that
throws, OR by asserting an existing endpoint's exception path. Prefer a minimal **test-only** `throw` endpoint
(e.g. `GET /api/_test/throw`) mapped only when a test flag is set, so production exposes no such route. The test
asserts: status 500, body `code = error.unexpected`, no `stack`/exception text in the body, and the `X-Trace-Id`
header present.

**Rationale**: A deterministic forced exception is the cleanest way to exercise `GlobalExceptionHandler` end-to-end;
gating the route keeps it out of production.

## D5 — Offline banner (edge case)

**Decision**: A `ConnectivityService` exposes an `isOnline` signal seeded from `navigator.onLine` and updated on
`window` `online`/`offline` events; a global `OfflineBanner` component (in the app shell) shows "Brak połączenia z
internetem" (`role="status"`) while offline and clears on restore. Design tokens, ≥360px.

**Rationale**: Minimal, framework-native connectivity signal satisfies FR-009/SC-006 without polling.

## D6 — Reused, unchanged (AC-2 / AC-3)

**Decision**: No behavioural change to the invalid-key-mid-session path (`AnswerGenerationFailure.InvalidKey →
settings.invalid_api_key` via the SSE `error` event; history untouched) or the provider timeout/unavailable path
(`chat.provider_unavailable` / `chat.provider_rate_limited` + `ChatStore.retry`; demo `429 + Retry-After`). US-19
adds/keeps regression tests and routes their messages through the shared dictionary; the Anthropic resilience
policies are unchanged.

**Rationale**: AC-2/AC-3 already pass; the story's job is to consolidate the message surface and lock the behaviour
with tests, not to re-implement it.
