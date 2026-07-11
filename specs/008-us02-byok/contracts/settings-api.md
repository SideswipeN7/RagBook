# Contract — Settings API (US-02 BYOK)

Base path `/api/settings/api-key`. Session is carried by the `ragbook_session` cookie (US-01); all responses carry `Cache-Control: no-store`. Errors follow RFC 9457 ProblemDetails with `extensions.code` (the stable error code) and a `traceId`.

## POST /api/settings/api-key — save & validate

Saves the user's Anthropic key after validating it upstream. Body only, HTTPS only.

**Request body**
```json
{ "apiKey": "sk-ant-api03-XXXXXXXXXXXXXXXXXXXXXXXX" }
```

**Responses**

| Status | When | Body |
|---|---|---|
| `200 OK` | key valid & stored | `{ "status": "active", "maskedKey": "sk-ant-api03-…B7fA" }` |
| `400 Bad Request` | empty / malformed / provider-rejected key (`settings.invalid_api_key`) | ProblemDetails |
| `429 Too Many Requests` | per-session throttle exceeded (`settings.too_many_attempts`) | ProblemDetails |
| `503 Service Unavailable` | provider unreachable during validation (`settings.validation_unavailable`) | ProblemDetails |

- On any non-200, **no key is stored** and prior state is unchanged.
- Full key is never echoed back — only the mask.

**Behavioral rules**
- Throttle is checked **before** the upstream validation call; a throttled request does not reach the provider.
- Malformed/empty keys are rejected **without** an upstream call (`settings.invalid_api_key`).
- Validation distinguishes provider *rejection* (→ 400) from provider *unavailability* (→ 503).

## GET /api/settings/api-key — status

Returns whether the session has an active key and, if so, its mask.

**Responses**

| Status | Body |
|---|---|
| `200 OK` (no key) | `{ "status": "none" }` |
| `200 OK` (key present) | `{ "status": "active", "maskedKey": "sk-ant-api03-…B7fA" }` |

- Never returns the full key. Idempotent, safe. `no-store`.
- Cross-session: session B always sees `none` for a key stored by session A.

## DELETE /api/settings/api-key — remove

Removes any stored key for the session.

**Responses**

| Status | When |
|---|---|
| `204 No Content` | key removed, or no key was present (idempotent) |

- After delete, `GET` returns `{ "status": "none" }` and generation is blocked again.

## Internal seam — generation guard (for US-14)

Not an HTTP endpoint in US-02. `IAnthropicClientFactory.CreateForSession()`:

- no active key → `Result.Failure(settings.api_key_missing)` (→ 401 when a future chat endpoint surfaces it).
- active key → `Result.Success(<client handle>)`.

US-02 delivers and unit-tests this failure path; US-14 wires it into the chat endpoint.

## Security invariants (contract-level)

- `Cache-Control: no-store` on **all** responses above.
- Full key value never present in any response body or log.
- `maskedKey` present iff `status == "active"`.
