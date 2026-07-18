# Quickstart — Validate US-19 (error handling)

Prerequisites: Docker (Testcontainers); .NET 10 SDK; Node + Edge/Chrome for Karma.

## 1. Domain / Application

```bash
dotnet test tests/RagBook.Domain.Tests/RagBook.Domain.Tests.csproj -c Debug
```
Proves: `ErrorStatusMapper.ToStatusCode` maps **every** `ErrorType` to a valid (non-zero) HTTP status — a new enum
value without a mapping fails the test.

## 2. Integration (Testcontainers)

```bash
docker info >/dev/null 2>&1 || echo "start Docker first"
dotnet test tests/RagBook.Api.IntegrationTests/RagBook.Api.IntegrationTests.csproj -c Debug
```
Proves: **AC-5** — a forced unhandled exception (`GET /api/_test/throw`) → `500` ProblemDetails with
`code = error.unexpected`, **no** stack trace, same shape as a domain error; **AC-4** — the response carries an
`X-Trace-Id` header equal to the body `traceId`, and the same id is captured in the server logs for that request;
any error response (e.g. a `404`/`409` from an existing endpoint) also carries `X-Trace-Id`.

## 3. Frontend

```bash
cd src/Web && CHROME_BIN="/c/Program Files (x86)/Microsoft/Edge/Application/msedge.exe" \
  npm run test -- --watch=false --browsers=ChromeHeadless
```
Proves: **AC-1** — `messageForCode` returns a dedicated Polish message for **every** stable backend code, and the
completeness spec fails if any known code is missing; the fallback is used only for an unknown code; the six stores
resolve messages through the shared dictionary (no local maps). The **offline banner** appears when `offline` fires
and clears on `online`. The invalid-key and provider-unavailable messages render with their recovery action.

## 4. Manual (AppHost)

```bash
dotnet run --project src/RagBook.AppHost
```
- Trigger any error (e.g. upload an unsupported file) → a readable Polish message; open dev-tools → the response has
  an `X-Trace-Id` header.
- Toggle the browser offline → the offline banner appears; back online → it clears.

## Acceptance mapping

| AC | Validated by |
|---|---|
| AC-1 catalog + one dictionary | §3 completeness spec + stores-use-shared |
| AC-2 invalid key mid-session | §3 message + settings action (history intact) |
| AC-3 provider timeout/unavailable + retry | §3 provider-unavailable message + retry · 429 wait |
| AC-4 correlation id | §2 `X-Trace-Id` == `traceId` + in logs |
| AC-5 no raw exceptions | §2 forced throw → 500 no-stack ProblemDetails |
