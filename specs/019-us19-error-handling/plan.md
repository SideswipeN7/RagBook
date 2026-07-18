# Implementation Plan: Obsługa błędów i stany brzegowe — error handling (US-19)

**Branch**: `019-us19-error-handling` | **Date**: 2026-07-18 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/019-us19-error-handling/spec.md`

## Summary

A consolidation story. The backend error contract already exists (one `ProblemResults` funnel → RFC 9457
ProblemDetails with a stable `module.snake_case` `code` + `traceId`; a `GlobalExceptionHandler` that returns
`error.unexpected` 500 with no stack trace; `ErrorStatusMapper`). US-19 closes the gaps: (1) **one shared frontend
dictionary** `core/error-messages.ts` (code→PL + `messageForCode`) that every store consumes — replacing the six
duplicated maps and covering every backend code, with a **completeness test**; (2) a **unified correlation id** —
all three ProblemDetails builders emit the W3C `Activity` trace id (fallback `TraceIdentifier`) and a middleware
stamps it as an `X-Trace-Id` response header, so the same id is in the response and (via OTel) the logs; (3) a
**global offline banner**; and (4) the **README error-code table**. The stable codes are unchanged; AC-2/AC-3/AC-5
are already met and get regression tests.

## Technical Context

**Language/Version**: C# (.NET 10) backend; TypeScript / Angular 20 frontend.

**Primary Dependencies**: existing `ProblemResults` / `BulkProblemResults` / `GlobalExceptionHandler` /
`ErrorStatusMapper`; the module `*Errors` catalogs; OpenTelemetry (ServiceDefaults, `IncludeScopes`); the six
frontend stores (chat/tree/selection/document-upload/api-key + the `document-tree` component map); the SSE chat
error path (US-14).

**Storage**: none — no schema change, no migration.

**Testing**: xUnit + FluentAssertions (Domain/Application: `ErrorStatusMapper` completeness); Testcontainers
(Integration: `X-Trace-Id` header == body `traceId`, present in logs via a captured logger; a forced-exception test
endpoint → 500 ProblemDetails, no stack trace); Karma (the shared dictionary is complete for every code;
`messageForCode`; the offline banner shows/hides; stores use the shared helper).

**Target Platform**: Linux container backend; evergreen browser SPA.

**Project Type**: Web (modular-monolith .NET backend + Angular SPA).

**Performance Goals**: no runtime cost beyond one response header + a static dictionary lookup.

**Constraints**: keep stable `module.snake_case` codes; one correlation-id source (Activity trace id) across all
three builders + header; no stack trace ever in a response; single frontend dictionary with one fallback for
unknown codes only; design tokens, ≥360px, no native dialogs. `ChatStore` keeps raw `fetch`.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **I. Vertical-Slice Modular Monolith** ✅ — the change is cross-cutting infrastructure (API ProblemDetails +
  middleware) and frontend shared code; no module boundaries crossed. The per-module `*Errors` catalogs are the
  source of truth; a small `ErrorCatalog` enumeration (for the completeness test) references them.
- **II. CQRS + Result Contract** ✅ — the `Result → ProblemDetails{code,traceId,detail,status}` contract is
  preserved and made consistent: all three builders share one trace-id source. No new error shapes.
- **III. Data Isolation** ✅ — no data access changed; ProblemDetails never leaks another session's data (existing
  not-found semantics unchanged).
- **IV. Test-First** ✅ — Domain (`ErrorStatusMapper` covers every `ErrorType`), Integration (`X-Trace-Id` ==
  `traceId` + in logs; forced exception → 500 no-stack-trace ProblemDetails), Angular (dictionary completeness vs
  the full code list; `messageForCode`; offline banner; stores use the shared helper). Red→Green.
- **V. Providers** ✅ — Anthropic resilience policies unchanged (validation retry; generation infinite-timeout, no
  retry — streaming). No provider work.
- **VI. Auditing & Time** ✅ — the correlation id is the trace id; no time/secret handling changes.
- **VII. Secrets** ✅ — no secrets. **VIII. Ops** ✅ — no migration; the `X-Trace-Id` header aids operability.
- **IX. Frontend & Design System** ✅ — one shared dictionary + a global offline banner (design tokens, a11y
  `role="status"`/`alert`), ≥360px; the taxonomy (inline validation / toast / empty state / retry panel) is honoured
  by the existing components consuming the shared helper.

**Result: PASS** — no violations; Complexity Tracking empty. Both clarified decisions (shared dictionary; Activity
trace id + `X-Trace-Id`) are the minimal, standards-based options.

## Project Structure

### Documentation (this feature)

```text
specs/019-us19-error-handling/
├── plan.md, research.md, data-model.md, quickstart.md
├── contracts/error-contract.md
├── checklists/requirements.md
└── tasks.md   (/speckit-tasks)
```

### Source Code (repository root)

```text
src/RagBook.API/
├── ProblemDetails/
│   ├── CorrelationId.cs              # static: Current(httpContext) = Activity.Current?.Id ?? TraceIdentifier
│   ├── ProblemResults.cs             # traceId ← CorrelationId (Activity id) — unchanged value in practice
│   ├── BulkProblemResults.cs         # traceId ← CorrelationId
│   └── GlobalExceptionHandler.cs     # traceId + log ← CorrelationId (was TraceIdentifier)
├── Middleware/TraceHeaderMiddleware.cs   # stamps X-Trace-Id on the response (OnStarting)
└── Program.cs                        # app.UseMiddleware<TraceHeaderMiddleware>() early; (test-only forced-error endpoint under a flag)

src/Web/src/app/
├── core/error-messages.ts            # ERROR_MESSAGES (all codes) + messageForCode(code, fallback?)
├── core/error-messages.spec.ts       # completeness vs the full code list
├── core/{chat,tree,selection,document-upload,api-key}.store.ts  # drop local maps → messageForCode
├── documents/tree/document-tree.ts   # drop FOLDER_ERROR_MESSAGES → messageForCode
├── core/connectivity.service.ts      # navigator.onLine + online/offline signals
└── shared/offline-banner/*           # global "Brak połączenia" banner (used in the app shell)

docs/features/README.md               # error-code catalog table (code → meaning → UI behaviour)
```

**Structure Decision**: Backend adds a tiny `CorrelationId` helper + a `TraceHeaderMiddleware` and points the three
ProblemDetails builders at the one source. Frontend adds `error-messages.ts` (the single dictionary + helper) and a
connectivity service + offline banner; the six stores/components drop their local maps. The README gains the code
table. No migration.

## Complexity Tracking

*No constitution violations — no entries.*
