# Quickstart — Validate US-18 (historia rozmowy)

Prerequisites: Docker running (Testcontainers); .NET 10 SDK; Node + Edge/Chrome for Karma. No real Anthropic key.

## 1. Domain (cheapest tier)

```bash
dotnet test tests/RagBook.Domain.Tests/RagBook.Domain.Tests.csproj -c Debug
```
Proves: `Conversation` title = first question truncated to 60; `ConversationHistory.SelectRecent` returns the last
N `(user, assistant)` pairs in order.

## 2. Application

```bash
dotnet test tests/RagBook.Application.Tests/RagBook.Application.Tests.csproj -c Debug
```
Proves: Create/List/Get/Delete handlers return `Result`/DTOs; `PromptBuilder.Build(question, chunks, history)`
prepends the recent turns and includes at most N pairs; the `ChatTurnCompleted` handler persists an assistant
`Message` with state + sources.

## 3. Integration (Testcontainers)

```bash
docker info >/dev/null 2>&1 || echo "start Docker first"
dotnet test tests/RagBook.Api.IntegrationTests/RagBook.Api.IntegrationTests.csproj -c Debug
```
Proves: create → ask → the user + assistant messages persist with state + `sources_json`; `GET /{id}` returns
them ordered; **cross-session `GET /{id}` → 404**; a follow-up ask builds a prompt containing prior turns; delete
cascades to messages; a conversation whose scope points at a deleted folder loads, but a new ask → `ScopeNotFound`.

## 4. Frontend

```bash
cd src/Web && CHROME_BIN="/c/Program Files (x86)/Microsoft/Edge/Application/msedge.exe" \
  npm run test -- --watch=false --browsers=ChromeHeadless
```
Proves: the conversation list renders + switches; "Nowa rozmowa" creates an empty conversation (scope Wszystkie);
opening a conversation renders messages with saved states (NoAnswerFound / Interrupted) and clickable citations
(from stored sources); delete asks for confirmation via the design-system dialog (no `window.confirm`).

## 5. Manual (AppHost)

```bash
dotnet run --project src/RagBook.AppHost
```
- Ask about several points → follow up "rozwiń punkt drugi" → the answer tracks the right point.
- Reload → the conversation is still in the list; reopen → messages + citations intact.
- "Nowa rozmowa" → empty thread; previous stays listed. Delete → confirm dialog → it's gone.

## Acceptance mapping

| AC | Validated by |
|---|---|
| AC-1 multi-turn context | §2 prompt-with-history · §3 follow-up prompt · §5 manual |
| AC-2 new conversation | §4 new + §3 create |
| AC-3 load history w/ states + citations | §3 load · §4 render |
| AC-4 bounded history (N pairs) | §1/§2 last-N-pairs |
| AC-5 session isolation | §3 cross-session 404 |
