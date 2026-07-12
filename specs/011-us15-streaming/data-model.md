# Phase 1 — Data Model: US-15 streaming chat UI

**No persisted entities and no migration.** Client-side view state + one config value + a reused SSE contract.

## Client view state (Angular signals)

### ChatExchange

| Field | Type | Notes |
|---|---|---|
| `id` | string | client id (stable key for the list) |
| `question` | string | the user's question |
| `scope` | ChatScopeSelection | the scope it was asked in |
| `status` | `'streaming' \| 'complete' \| 'interrupted' \| 'error'` | lifecycle |
| `answer` | string | accumulating answer text (grows per `token`) |
| `sources` | Source[] | from the `sources` event |
| `groundsFound` | boolean | from `done`; `false` → neutral no-basis note (US-17 refines) |
| `errorMessage` | string? | PL message when `status === 'error'` |

### ChatScopeSelection

| Field | Type | Notes |
|---|---|---|
| `type` | `'all' \| 'folder' \| 'document'` | which boundary |
| `targetId` | string? | folder/document id (absent for `all`) |
| `label` | string | display label (e.g. "Wszystkie dokumenty", folder/file name) for the chip |

### Source (from the `sources` event — US-14 `SourceDto`)

| Field | Type |
|---|---|
| `number` | int |
| `documentId` | string |
| `fileName` | string |
| `pageNumber` | int? |

(US-15 renders these as a plain list; clickable `[n]`→document is US-16.)

## Consumed SSE contract (US-14, unchanged)

`POST /api/chat/ask` `{ question, scope:{type,targetId} }` → `text/event-stream`:

| Event | Data | Client action |
|---|---|---|
| `sources` | `[{number,documentId,fileName,pageNumber}]` | set the exchange's `sources` |
| `token` | `{ text }` | append `text` to `answer` |
| `done` | `{ groundsFound }` | `complete` (+ no-basis note when false) |
| `error` | `{ code }` | `error` + mapped PL message |
| `:` comment (heartbeat) | — | ignored by the parser |

Pre-stream failure = a non-2xx ProblemDetails JSON (`code`) → `error`. Stream ends without `done` → `error` ("interrupted").

## Configuration

`RagOptions` (`Rag` section) — **extended**:

| Property | Type | Default | Meaning |
|---|---|---|---|
| `StreamHeartbeatSeconds` | int | 15 | keep-alive comment interval during a long generation |

## Error code → PL message (client map)

| Code | Message (PL, indicative) |
|---|---|
| `settings.api_key_missing` | "Skonfiguruj klucz API, aby zadać pytanie." |
| `settings.invalid_api_key` | "Klucz API został odrzucony — sprawdź go w ustawieniach." |
| `chat.provider_rate_limited` | "Zbyt wiele zapytań — spróbuj ponownie za chwilę." |
| `chat.provider_unavailable` | "Usługa AI jest chwilowo niedostępna. Spróbuj ponownie." |
| `chat.scope_not_found` | "Wybrany zakres już nie istnieje — przełącz na 'Wszystkie'." |
| `chat.invalid_question` | "Pytanie jest puste lub zbyt długie." |
| (unknown / interrupted) | "Coś poszło nie tak podczas generowania. Spróbuj ponownie." |

## Invariants

- At most **one** exchange is `streaming` at a time; a new ask or `stop()` aborts the previous.
- `answer` only grows during `streaming`; a `token` after `stop`/abort is never applied.
- An `error` or a stream-without-`done` always leaves a visible message + a Try-again affordance (never a silent cut).
- Only **ready** documents are offered as a file scope.
- The thread lives in the client only and resets on reload (persistence = US-18).
