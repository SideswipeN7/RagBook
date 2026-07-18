# Quickstart — Validate US-03 (tryb demo)

Prerequisites: Docker (Testcontainers); .NET 10 SDK; Node + Edge/Chrome for Karma. An application key is only
needed for a real end-to-end demo answer against Anthropic; tests use the fake embedding/generation seams.

## 1. Application

```bash
dotnet test tests/RagBook.Application.Tests/RagBook.Application.Tests.csproj -c Debug
```
Proves: the per-session counter refuses beyond `MaxQuestionsPerSession` (`chat.demo_limit_reached`) and reports
`remaining`; the answer generator uses the **demo** key when `GroundedContext.IsDemo` and the **session** key
otherwise; the ask pipeline sets `IsDemo` for the demo scope; an upload made while the demo scope is active is
stored as a **user** document.

## 2. Integration (Testcontainers)

```bash
docker info >/dev/null 2>&1 || echo "start Docker first"
dotnet test tests/RagBook.Api.IntegrationTests/RagBook.Api.IntegrationTests.csproj -c Debug
```
Proves: the **seeder** creates the demo documents on a clean DB and is a **no-op** on a second run (idempotent,
fixed ids); demo documents are visible from **any** session (`GET /api/tree` `demo[]`); a demo ask **without a
session key** returns an answer + citations drawn from demo documents; deleting / moving / bulk-operating a demo
document is refused as `document.read_only`; demo documents are **excluded from quota** (a full session can still
upload its whole allowance); the **per-IP** limit returns `429` with a `Retry-After` header; the per-session limit
returns `429 chat.demo_limit_reached`.

## 3. Frontend

```bash
cd src/Web && CHROME_BIN="/c/Program Files (x86)/Microsoft/Edge/Application/msedge.exe" \
  npm run test -- --watch=false --browsers=ChromeHeadless
```
Proves: the tree renders a **read-only Demo section** (badge, no move/delete/checkbox controls) from `demo[]`; the
"Dokumenty demo" scope option posts `scope: { type: "demo" }`; the demo banner shows while active; the counter shows
"X / N pytań demo" from `GET /api/demo/status` and decrements on a demo ask; `chat.demo_limit_reached` (+ BYOK
nudge), a `429` retry message, and `chat.demo_unavailable` map to readable messages.

## 4. Manual (AppHost)

```bash
Demo__ApplicationKey=<key> dotnet run --project src/RagBook.AppHost
```
- Open a fresh browser (no API key set) → a **Demo** section lists the seeded documents.
- Pick "Dokumenty demo", ask a question → a streamed answer with citations to the demo docs, no key entry.
- Ask until the per-session limit → the counter hits 0 and a BYOK nudge appears; further demo asks are refused.
- Try to delete/move a demo document → no such controls; the API refuses it as read-only.

## Acceptance mapping

| AC | Validated by |
|---|---|
| AC-1 keyless demo answer | §2 demo-ask-without-key · §3 scope option/banner · §4 |
| AC-2 per-session limit | §1 counter · §2 429 chat.demo_limit_reached · §3 counter + nudge |
| AC-3 per-IP rate limit | §2 429 + Retry-After · §3 retry message |
| AC-4 read-only demo | §2 read_only refusal · §3 no mutating controls |
| AC-5 demo not in quota | §2 quota-exclusion regression |
