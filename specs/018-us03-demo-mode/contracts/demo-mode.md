# Contract — Demo mode (US-03)

Demo reuses the existing chat SSE endpoint with a new **scope**, adds a small **status** read, and extends the
**tree** read with a global demo-doc list. No new mutation endpoints.

## `POST /api/chat/ask` — demo scope (existing endpoint, new scope)

```jsonc
{ "question": "…", "scope": { "type": "demo" }, "conversationId": "<guid>" }
```

Behaviour when `scope.type == "demo"` (order of guards):

1. **Per-IP hourly limit** (`IDemoIpThrottle`): over limit → **`429`** with a `Retry-After: <seconds>` header, no
   generation. (BYOK / non-demo asks are never IP-throttled.)
2. **Per-session lifetime limit** (`IDemoQuestionCounter`): at/over `MaxQuestionsPerSession` → **`429`**
   ProblemDetails `code: chat.demo_limit_reached`, no generation.
3. **Application key**: unset → **`503`** ProblemDetails `code: chat.demo_unavailable` (never a raw 500).
4. Otherwise: the **session BYOK key guard is skipped**; retrieval runs over `Origin == Demo` (no session
   predicate); the answer streams on the **application key** as the normal SSE (`sources` → `token`* →
   `done`), with citations to demo documents. A successful ask **consumes** one from the session counter.

A provider failure before the first delta → ProblemDetails (`chat.provider_unavailable` → 503, shown as "demo
temporarily unavailable"); mid-stream → SSE `error` event (existing US-14 behaviour).

Non-demo scopes (`all` / `folder` / `document`) are unchanged, including the `settings.api_key_missing` (401) guard.

## `GET /api/demo/status`

```jsonc
{ "asked": 3, "max": 10, "remaining": 7, "available": true }
```
- `available` = an application key is configured (so demo can generate at all).
- Drives the "X / N pytań demo" counter and the BYOK nudge when `remaining == 0`.

## `GET /api/tree` — extended

The existing response gains a **`demo`** array (global demo documents, read-only), alongside the session's own
`folders` / `documents`:
```jsonc
{ "folders": [ … ], "documents": [ … ], "demo": [ { "id", "fileName", "contentType", "sizeBytes", "status", "chunkCount", "uploadedAt" } ] }
```
The user's own `documents` list still excludes `Origin == Demo` (unchanged); `demo` is the separate global list.

## Read-only (existing contracts, demo-applicable)

- `DELETE /api/documents/{id}`, `PATCH /api/documents/{id}/folder`, `POST /api/documents/bulk-move|bulk-delete` on a
  demo document → refused as **`document.read_only`** (409 single / `422` failures for bulk). US-03 adds regression
  coverage and, if the single-delete path lacks the guard, closes it.

## Invariants

- The application key is never sent to the client (no endpoint echoes it; `/api/demo/status` exposes only a boolean).
- Demo documents are visible in **every** session and mutable by **none**.
- Demo generation and reads never touch another session's data; user reads never return demo docs in the own-docs
  list.
- Both demo limits are configuration-driven; over-limit is deterministic (429, with `Retry-After` for the IP limit).

## Frontend consumption

- Chat scope selector offers **"Dokumenty demo"** → posts `scope: { type: "demo" }`; a demo banner shows while
  active; the counter reads `GET /api/demo/status`; `chat.demo_limit_reached` shows the count + BYOK nudge; a `429`
  with `Retry-After` and `chat.demo_unavailable` show readable messages.
- The tree renders a read-only **Demo** section from `demo[]` — a badge, no move/delete/checkbox controls.
