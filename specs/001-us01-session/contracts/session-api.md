# Phase 1 Contracts — US-01 Session API

All endpoints run behind `SessionMiddleware`: every response — success or error — carries a
`Set-Cookie: ragbook_session=<guid>; HttpOnly; Secure; SameSite=Strict; Path=/; Expires=<+30d>`
header (issued on first visit, refreshed thereafter). Errors follow RFC 9457 ProblemDetails with a
stable `code`.

## GET /api/session — current session state (AC-1, AC-2)

- **Purpose**: cheapest endpoint that proves a session is issued/refreshed and returns empty-session
  application state.
- **Request**: no body. Cookie optional.
- **Response `200`**:
  ```json
  { "isNew": true, "resourceCount": 0 }
  ```
  `isNew` = true when this request minted the session. `Set-Cookie` present on every response.
- **Behavior**: missing/forged/expired cookie → `isNew: true`, new session, `resourceCount: 0`
  (never an error — edge cases in spec).

## POST /api/resources — create a session-owned resource (setup for AC-2/AC-3)

- **Request**:
  ```json
  { "name": "My first resource" }
  ```
- **Response `201`**: `{ "id": "<guid>" }`, `Location: /api/resources/<guid>`. `UserSessionId` is
  stamped server-side from the session — never accepted from the client.
- **Errors**: `400` `validation.failed` (empty/too-long name) via ProblemDetails.

## GET /api/resources/{id} — read one (AC-3)

- **Response `200`**: `{ "id", "name", "createdAt" }` when the resource belongs to the caller's
  session.
- **Response `404`** `session.resource_not_found`: when the id does not exist **or** belongs to
  another session — the two are indistinguishable (never 403; existence is not disclosed).

## GET /api/resources — list own resources (AC-3)

- **Response `200`**: `[{ "id", "name", "createdAt" }]` — only the caller's own resources; another
  session's resources never appear. Empty array for a fresh session.

## Cross-cutting

- **Unknown fault** → sanitized `500` `error.unexpected` + `traceId` (no stack leaked).
- **Frontend interceptor** maps any `404` to a "resource does not exist" experience (§IX); it holds
  no isolation logic.
