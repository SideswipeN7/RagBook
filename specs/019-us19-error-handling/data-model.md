# Phase 1 Data Model — US-19

No entities, no schema, no migration. US-19 touches the error-response contract and frontend message resolution.

## Error-response contract (existing, made consistent)

| Field | Source | Change |
|---|---|---|
| `status` | `ErrorStatusMapper.ToStatusCode(error.Type)` (or 422 for bulk) | unchanged |
| `code` | `error.Code` (`module.snake_case`) | unchanged |
| `detail` | `error.Message` (static, sanitized) | unchanged |
| `traceId` (extension) | **`Activity.Current?.Id`** across all 3 builders | `GlobalExceptionHandler` switches from `TraceIdentifier` → the shared source |
| `X-Trace-Id` (response header) | `CorrelationId.Current(httpContext)` via middleware | **new** — equals `traceId` |
| `failures` (extension, bulk) | `[{ id, code }]` | unchanged |

## Backend additions

| Item | Shape |
|---|---|
| `CorrelationId` | `static string Current(HttpContext ctx) => Activity.Current?.Id ?? ctx.TraceIdentifier;` |
| `TraceHeaderMiddleware` | stamps `X-Trace-Id` on the response (`Response.OnStarting`) with `CorrelationId.Current`. |
| `ErrorStatusMapper` test | asserts every `ErrorType` → a non-zero status (guards enum growth). |
| Test-only throw endpoint | `GET /api/_test/throw` mapped only under a test flag (AC-5). |

## Error-code catalog (the wire contract — documented in README)

| Module | Codes |
|---|---|
| Session | `session.resource_not_found`, `session.name_required`, `session.resource_already_exists`, `session.concurrency_conflict` |
| Documents | `document.unsupported_file_type`, `document.empty_file`, `document.not_found`, `document.read_only`, `document.bulk_validation_failed`, `document.bulk_empty`, `document.bulk_too_large` |
| Quota | `quota.exceeded`, `quota.conflict`, `quota.invalid_size`, `quota.total_size_exceeded`, `quota.file_too_large` |
| Folders | `folder.invalid_name`, `folder.max_depth_exceeded`, `folder.duplicate_name`, `folder.not_empty`, `folder.not_found`, `folder.conflict`, `folder.circular_move` |
| Chat | `chat.scope_not_found`, `chat.invalid_question`, `chat.provider_rate_limited`, `chat.provider_unavailable`, `chat.conversation_not_found` |
| Demo | `chat.demo_limit_reached`, `chat.demo_rate_limited`, `chat.demo_unavailable` |
| Settings | `settings.invalid_api_key`, `settings.validation_unavailable`, `settings.api_key_missing`, `settings.too_many_attempts` |
| Global | `validation.failed`, `error.unexpected` |

## Frontend state

- `core/error-messages.ts`: `ERROR_MESSAGES: Record<string,string>` (every code above) + `messageForCode(code?, fallback?)`.
- `core/connectivity.service.ts`: `isOnline` signal (from `navigator.onLine` + `online`/`offline` events).
- The six stores/components drop their local maps and call `messageForCode`.
