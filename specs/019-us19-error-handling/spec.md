# Feature Specification: Obsługa błędów i stany brzegowe — error handling (US-19)

**Feature Branch**: `019-us19-error-handling`

**Created**: 2026-07-18

**Status**: Draft

**Input**: User description: US-19 — in every error situation the user gets a readable, specific message with a
possible corrective action — never a raw exception, a bodyless 500, or a broken UI. Mostly consolidation of the
error handling prior stories already built.

## Context

The backend error contract is already strong: every domain failure flows through **one** funnel to an RFC 9457
ProblemDetails carrying a stable `module.snake_case` `code` + a `traceId`; unhandled exceptions are caught by a
global handler that returns ProblemDetails (`error.unexpected`, 500) and **never** leaks a stack trace. What is
missing is **consolidation and a few gaps**: the frontend maps error codes to Polish messages in **six** separate,
duplicated, incomplete dictionaries (the main gap — no single source, some codes unmapped, divergent wording); the
**correlation id** is only partially wired (two inconsistent id sources, no response header, logged only for
unhandled 500s); there is **no offline banner**; and the DoD's **README error-code table** does not exist. US-19
closes these, keeping the shipped `module.snake_case` codes unchanged (the PascalCase names in the source doc are
illustrative, not binding).

## Clarifications

### Session 2026-07-18

- Q: How should the frontend error-message consolidation be structured? → A: **Shared dictionary, stores consume
  it** — one `core/error-messages.ts` (a code→PL map + a `messageForCode(code, fallback?)` helper); the six existing
  stores drop their local maps and call the shared helper (lowest-risk, fully satisfies AC-1 + the completeness
  test). `ChatStore` keeps its raw-`fetch` streaming path but resolves messages through the shared helper. No
  central HTTP ProblemDetails interceptor (out of scope for this story).
- Q: How should the correlation id be sourced and exposed? → A: **W3C Activity trace id + `X-Trace-Id` header** —
  all three ProblemDetails builders (`ProblemResults`, `BulkProblemResults`, `GlobalExceptionHandler`) unify on
  `Activity.Current?.Id` (fallback `HttpContext.TraceIdentifier`); a middleware stamps that id as an `X-Trace-Id`
  response header. OTel already correlates logs with the trace id (`IncludeScopes`), so the same id appears in the
  response (header + `traceId`) and the server logs. No bespoke GUID / logger-scope mechanism.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Every error has a clear message (Priority: P1) 🎯 MVP

Whatever fails, the user sees a specific Polish message (with a corrective action where one exists) — never a raw
code, a bodyless failure, or a broken view.

**Why this priority**: A trustworthy product never shows the user a raw error; a single, complete message dictionary
is what guarantees that for every known failure.

**Independent Test**: For every stable API error code, the frontend renders a dedicated Polish message (not the
unknown-error fallback); a completeness test asserts the dictionary covers the full code list.

**Acceptance Scenarios**:

1. **Given** any stable API error code returns from the backend, **When** the frontend handles it, **Then** it shows
   a dedicated Polish message with a sensible action; the unknown-error fallback is used **only** for codes not in
   the catalog.
2. **Given** the set of duplicated per-feature message maps, **When** the app is built, **Then** there is a **single**
   shared code→message dictionary and no divergent wording for the same code.

---

### User Story 2 - Invalid key mid-session (Priority: P1)

A key invalidated by the provider mid-session produces a clear "key was rejected" message pointing to settings,
without touching the conversation history.

**Why this priority**: A rejected key is a common, recoverable failure; the user must know exactly what to fix and
lose no work.

**Independent Test**: With a key that the provider rejects, ask a question → a "key was rejected — check settings"
message appears and the conversation history is unchanged.

**Acceptance Scenarios**:

1. **Given** a key rejected by the provider during a session, **When** the user asks a question, **Then** a message
   says the key was rejected and points to settings, and the conversation history is untouched.

---

### User Story 3 - Provider timeout / unavailability (Priority: P1)

A provider timeout or 5xx yields a "temporarily unavailable — try again" panel with a retry that re-runs the last
question; a provider 429 with a retry hint tells the user how long to wait.

**Why this priority**: Transient provider failures must be recoverable in one click, not dead-ends.

**Independent Test**: Force a provider failure before the first token → a "temporarily unavailable" message with a
retry control that re-asks the last question; a 429 with a retry-after surfaces the wait.

**Acceptance Scenarios**:

1. **Given** a provider timeout or 5xx on a chat request, **When** it fails, **Then** the user sees a "temporarily
   unavailable — try again" message with a retry that re-runs the last question.
2. **Given** a provider (or demo) 429 carrying a retry hint, **When** it is shown, **Then** the message conveys when
   to try again.

---

### User Story 4 - Correlation id for support (Priority: P2)

An unexpected error shows the user a short report id, and that same id appears in the server logs (and on a response
header on every error), so a failure can be traced end-to-end.

**Why this priority**: Without a correlatable id, an "unexpected error" is unsupportable; one consistent id makes it
diagnosable.

**Independent Test**: Force an unhandled exception → the response carries a correlation id (header + body `traceId`),
and the same id is present in the server log for that request.

**Acceptance Scenarios**:

1. **Given** an `error.unexpected` failure, **When** the user sees the message, **Then** it includes a short report
   id, the same id is on the error response (header + `traceId`), and the same id is in the server log for that
   request.
2. **Given** any error response (domain or unexpected), **When** it is returned, **Then** it carries the correlation
   id header and its `traceId` extension come from the **same** source.

---

### User Story 5 - No raw exceptions ever (Priority: P1)

Any unhandled exception becomes a 500 ProblemDetails in the same shape as domain errors, with no stack trace in the
response.

**Why this priority**: Leaking a stack trace or an empty 500 is both a security and a trust failure.

**Independent Test**: Force an exception in a handler → the client gets a 500 ProblemDetails (stable code, no stack
trace), identical in shape to a domain error.

**Acceptance Scenarios**:

1. **Given** a handler that throws, **When** the response returns, **Then** it is a 500 ProblemDetails with a stable
   code and **no** stack trace, in the same format as domain failures.

---

### Edge Cases

- **Provider 429 with retry-after**: the message conveys the wait time.
- **Network loss in the SPA**: a global offline banner appears (driven by connectivity + failed requests) and clears
  when connectivity returns.
- **Unknown code**: a single neutral fallback message is shown (only for codes genuinely absent from the catalog).
- **Streaming chat failures**: a mid-stream provider failure surfaces as a readable message with retry (the raw
  streaming transport is unchanged).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The frontend MUST resolve error codes to Polish messages through a **single shared dictionary**; every
  stable backend code MUST have a dedicated entry, and the unknown-error fallback MUST be used **only** for codes not
  in the catalog.
- **FR-002**: The shared dictionary MUST replace the previously duplicated per-feature maps, with **one** agreed
  wording per code (no divergent messages for the same code).
- **FR-003**: A completeness check MUST verify the dictionary covers the **full** list of stable backend codes (so a
  newly added code without a message is caught).
- **FR-004**: An invalidated-key failure during a session MUST surface a "key was rejected — check settings" message
  and leave the conversation history unchanged.
- **FR-005**: A provider timeout / 5xx on a chat request MUST surface a "temporarily unavailable — try again"
  message with a retry that re-runs the last question; a 429 with a retry hint MUST convey the wait.
- **FR-006**: Every error response MUST carry a **correlation id** as an `X-Trace-Id` response header and as the
  `traceId` extension, both from the **same** source (the W3C `Activity` trace id, falling back to the request trace
  identifier), for domain failures and unexpected exceptions alike.
- **FR-007**: The same correlation id MUST appear in the server logs for that request (so a reported id is traceable).
- **FR-008**: Any unhandled exception MUST become a 500 ProblemDetails with a stable code and **no** stack trace, in
  the same shape as domain failures.
- **FR-009**: The SPA MUST show a global **offline banner** when connectivity is lost and clear it when restored.
- **FR-010**: The stable error codes MUST remain unchanged (`module.snake_case`); the documented catalog MUST list
  the **actual** codes, their meaning, and the UI behaviour.

### Key Entities

- **Error code**: a stable `module.snake_case` identifier on every failure response; the contract between backend and
  the frontend message dictionary.
- **Message dictionary**: the single frontend map from code → Polish message (+ the one fallback for unknown codes).
- **Correlation id**: one id per request, on the error response (header + `traceId`) and in the server logs.
- **ProblemDetails**: the RFC 9457 error body (`{ code, traceId, detail, status }`, plus extensions like `failures`).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of stable backend error codes map to a dedicated Polish message; the unknown fallback appears for
  0 known codes (asserted by a completeness test).
- **SC-002**: The frontend has exactly **one** error-message dictionary; 0 duplicated per-feature maps remain.
- **SC-003**: 100% of error responses (domain and unexpected) carry a correlation id header whose value equals the
  body `traceId`, and that id is present in the server log for the same request.
- **SC-004**: 100% of forced unhandled exceptions return a 500 ProblemDetails with no stack trace, in the same shape
  as a domain error.
- **SC-005**: A rejected-key mid-session and a provider-unavailable failure each show their dedicated message with
  the correct recovery action (settings link / retry) in 100% of cases, with conversation history intact.
- **SC-006**: The offline banner appears within a second of connectivity loss and clears on restore, every time.

## Assumptions

- Stable codes stay `module.snake_case`; the source doc's PascalCase names are illustrative. `error.unexpected` is
  the unexpected-error code (not renamed to `InternalError`).
- The correlation id is the request's trace identifier; the specific source + header name is a Clarifications
  decision. The Anthropic client resilience policies (validation retry; generation no-retry streaming) are unchanged.
- `ChatStore` keeps its raw-`fetch` streaming path (not migrated to the HTTP client); it consumes the same shared
  dictionary.

## Dependencies

- Cross-cutting over **US-02–US-18** (all merged) — the error contract, catalogs, exception handler, and per-store
  message maps this story consolidates.

## Out of Scope

- Telemetry / APM / alerting; internationalisation beyond Polish.
- Rewriting `ChatStore` from `fetch` to the HTTP client.
- A central HTTP ProblemDetails interceptor is a Clarifications decision, not assumed.
