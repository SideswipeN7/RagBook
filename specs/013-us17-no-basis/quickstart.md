# Quickstart — Validate US-17 (no-basis / refusal)

Prerequisites: Docker running (Testcontainers); .NET 10 SDK; Node + Edge/Chrome for Karma. No real Anthropic key
(all tests use the fake generator).

## 1. Domain rule (cheapest tier)

```bash
dotnet test tests/RagBook.Domain.Tests/RagBook.Domain.Tests.csproj -c Debug
```

Proves `GroundingPrompt.IsRefusal`: exact phrase → true; whitespace-padded → true; mid-text/partial/normal →
false.

## 2. Streaming state (integration)

```bash
docker info >/dev/null 2>&1 || echo "start Docker first"
dotnet test tests/RagBook.Api.IntegrationTests/RagBook.Api.IntegrationTests.csproj -c Debug
```

Proves:
- Generator scripted with the sentinel → `done.state == "no_answer"`, a `sources` event was sent (prompt-refusal).
- Off-topic question → `done.state == "no_answer"`, generator **not invoked**, **no** `sources` event
  (deterministic).
- Normal scripted answer → `done.state == "answered"`.
- `NoBasisEvalTests`: ≥10 (question, expected state) pairs over seeded docs all resolve to their expected state.

## 3. Frontend render variant

```bash
cd src/Web && CHROME_BIN="/c/Program Files (x86)/Microsoft/Edge/Application/msedge.exe" \
  npm run test -- --watch=false --browsers=ChromeHeadless
```

Proves a `done` with `state:"no_answer"` renders the neutral NoAnswerFound view (message + hints), distinct from
the error view, with „przeszukane fragmenty" shown only when sources were received, and no produced-answer
paragraph.

## 4. Manual (AppHost)

```bash
dotnet run --project src/RagBook.AppHost
```

- Ask something off-topic for your documents → immediate neutral „Nie znalazłem tego w dokumentach" + hints, no
  sources, no spinner delay (no model call).
- Ask an on-topic question the documents don't actually answer → after streaming, the message settles into the
  neutral state with a collapsible „przeszukane fragmenty".
- Ask a two-part question the docs partly cover → a normal answer with `[n]` citations that also names the gap.

## Expected acceptance mapping

| AC | Validated by |
|---|---|
| AC-1 deterministic cut-off (no LLM) | §2 off-topic case + `NoBasisEvalTests`; `Generator.Invoked == false` |
| AC-2 prompt refusal → NoAnswerFound | §2 sentinel case + §3 render |
| AC-3 distinct from error | §3 render (neutral vs error treatment) |
| AC-4 partial answer stays Normal | §1 rule + §2 normal/partial case |
| AC-5 threshold eval set | §2 `NoBasisEvalTests` (≥10 pairs); threshold documented in README + research.md |
