# Contract — Error handling (US-19)

No new business endpoints. US-19 standardises the **error-response contract** and adds one response header.

## Every error response (domain + unexpected)

RFC 9457 ProblemDetails body:
```jsonc
{
  "status": <int>,
  "detail": "<sanitized message>",
  "code": "<module.snake_case>",
  "traceId": "<W3C activity id>",
  "failures": [ { "id", "code" } ]   // only for document.bulk_validation_failed (422)
}
```
plus a response header:
```
X-Trace-Id: <same value as body traceId>
```

- The `X-Trace-Id` header is present on **every** response (error and success — it's harmless on success) and its
  value equals the body `traceId` on errors. Source: `Activity.Current?.Id` (fallback `TraceIdentifier`).
- **No** stack trace or exception text ever appears in the body.
- `error.unexpected` (500) is logged with the same trace id, so a reported id is traceable end-to-end.

## Status mapping (unchanged)

`Validation→400 · NotFound→404 · Conflict→409 · Unauthorized→401 · Forbidden→403 · RateLimited→429 · Unavailable→503
· Unexpected→500`; bulk validation failure → `422` (built directly). A provider/demo `429` may carry `Retry-After`.

## Frontend consumption

- One `messageForCode(code, fallback?)` resolves **every** stable code to its Polish message; the fallback is used
  only for codes absent from the dictionary (a completeness test forbids a known code falling through).
- Message taxonomy by surface (unchanged behaviour): validation → inline; operation failure → toast/notice; empty
  state (e.g. no-answer) → in-content (not an error); critical view failure → panel with **Retry**.
- Invalid key mid-session → the `settings.invalid_api_key` message points to settings; history intact.
- Provider timeout/5xx → the `chat.provider_unavailable` message + a retry that re-asks the last question; a `429`
  conveys the wait.
- Connectivity lost → a global offline banner ("Brak połączenia z internetem") appears and clears on restore.

## Test surface

- `GET /api/_test/throw` — mapped **only** under a test flag; throws to exercise `GlobalExceptionHandler` (AC-5).
  Asserts: `500`, `code = error.unexpected`, no stack trace, `X-Trace-Id` present and equal to body `traceId`.
