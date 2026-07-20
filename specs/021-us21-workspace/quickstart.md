# Quickstart — Validate US-21 (workspace redesign)

Staged: run per stage. Prereqs: Docker (Testcontainers); .NET 10; Node + Edge/Chrome.

## Stage 1 — shell + onboarding (frontend)

```bash
cd src/Web && CHROME_BIN="/c/Program Files (x86)/Microsoft/Edge/Application/msedge.exe" \
  npm run test -- --watch=false --browsers=ChromeHeadless
```
Proves: an onboarding step shows before the workspace (config: key or demo); once configured, a 4-column grid renders
(conversations collapsible | sources | chat | Studio); selecting a conversation updates all columns from one shared
signal; collapsing a column keeps the active selection. Existing tiers stay green.

## Stage 2 — per-conversation sources (backend + migration)

```bash
docker info >/dev/null 2>&1 || echo "start Docker"
dotnet test tests/RagBook.Application.Tests
dotnet test tests/RagBook.Api.IntegrationTests
cd src/Web && npm test
```
Proves: uploading in conversation A pins the document to A (`GET /api/tree?conversationId=A` shows it, B does not);
asking in A grounds only on A's sources; folders are per-conversation; drag-drop + bulk still work; deleting A
cascades its folders + documents + chunks (quota drops); the migration applies (the US-20 `migrate` step).

## Stage 3 — Studio summary

```bash
dotnet test tests/RagBook.Application.Tests   # summary handler grounds on the conversation's sources
dotnet test tests/RagBook.Api.IntegrationTests # POST /api/conversations/{id}/summary
cd src/Web && npm test                         # Studio tile renders the summary / empty state
```
Proves: with ready sources + a key/demo, the summary tile shows a generated overview; with no sources, a neutral
empty/disabled state; a foreign conversation → 404.

## Manual (compose)

```bash
docker compose up -d --build   # US-20 stack; open http://localhost:8080
```
Configure (or demo) → create a conversation → add sources → chat + "Podsumowanie" in Studio.

## Acceptance mapping

| AC | Stage | Validated by |
|---|---|---|
| AC-1 config-first + 4-column | 1 | shell/onboarding Karma · manual |
| AC-2 sources per conversation | 2 | upload-pinned + per-conversation ask (integration) |
| AC-3 Studio summary | 3 | summary endpoint + tile |
