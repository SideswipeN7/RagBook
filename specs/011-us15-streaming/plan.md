# Implementation Plan: Streaming odpowiedzi — chat UI + SSE hardening (US-15)

**Branch**: `011-us15-streaming` (git: `fm/us15-streaming`) | **Date**: 2026-07-12 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/011-us15-streaming/spec.md`

## Summary

Mostly **frontend**: an Angular chat surface that consumes US-14's `POST /api/chat/ask` SSE (`sources` → `token` → `done{groundsFound}` / `error{code}`) via **streaming fetch + a ReadableStream SSE parser** (native `EventSource` cannot POST). A signal-based `ChatStore` holds a **multi-turn thread** (in-memory exchanges: `streaming`/`complete`/`interrupted`/`error`), appends `token` text incrementally, sets sources on `sources`, and exposes `ask(question, scope)` (aborting any prior stream — one active generation, FR-008) and `stop()` (AbortController → the request aborts → the server's `CancellationToken` cancels generation, already wired in US-14). A `Chat` component renders the thread + the scope selector (All / folder / **ready** document, from `TreeStore`) as a chip, the input (disabled when `chatLocked`, US-02), Stop, error+Try-again, the no-basis note, and auto-scroll with detach. Error codes → PL messages via a `Record`. **Backend hardening** (small): a configurable periodic **keep-alive** SSE comment during long generations, and an integration test proving generation is **cancelled** when the client disconnects. No new backend contract, no migration. Citations (US-16), refusal UX (US-17), persistence (US-18) are out of scope.

## Technical Context

**Language/Version**: TypeScript / Angular (latest stable) — the bulk; C# (net10.0) for the small backend hardening.

**Primary Dependencies**: Frontend — the streaming **Fetch API** (`fetch` + `response.body` ReadableStream) + `AbortController` + `TextDecoder` (not Angular `HttpClient`, which does not surface a token stream); Angular Signals, standalone/OnPush. Reuses `ApiKeyStore` (`chatLocked`, US-02) + `TreeStore` (scope data, US-07). Backend — the existing US-14 `ChatEndpoints` SSE, extended with a heartbeat.

**Storage**: none (the thread is in the client; persistence is US-18). No migration.

**Testing**: Angular Karma/ChromeHeadless — `ChatStore` tests **stub the global `fetch`** to return a scripted `ReadableStream` of SSE bytes (HttpTestingController does not intercept fetch), asserting incremental append / done / error / abort→interrupted / stream-without-done→error; component tests with a mocked store. Backend — Testcontainers integration reusing US-14's fake generator: a **heartbeat** test (tiny configured interval + a delaying fake → a comment appears) and a **cancellation** test (abort the client request mid-stream → the fake observes cancellation). No test hits Anthropic (§V).

**Target Platform**: Angular SPA; Linux container backend.

**Project Type**: Web (Angular SPA + .NET backend).

**Performance Goals**: First token painted promptly after generation starts; smooth incremental append (no whole-block re-render); keep-alive prevents proxy idle-timeout cut.

**Constraints**: One active generation per chat. Question never in the URL (US-14, POST body). No `window.confirm`/native dialogs; design tokens only (no inline hex); works ≥360px.

**Scale/Scope**: One streamed answer at a time; a modest in-memory thread per session.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Assessment |
|---|---|
| I. Vertical-slice modular monolith | ✅ Backend change is confined to the existing `Chat` streaming endpoint (heartbeat). No new module. |
| II. CQRS + Result contract | ✅ No new command/query; the SSE endpoint is unchanged in contract (US-14). |
| III. Data isolation by session | ✅ Reuses US-14/US-13 session scoping; the frontend holds no isolation logic (backend-managed cookie). |
| IV. Test-First (Red→Green) | ✅ `ChatStore` behaviors via failing Karma tests (stubbed fetch); backend heartbeat + cancellation via failing integration tests first. |
| V. External providers — resilience + cache | ✅ No test hits Anthropic — frontend stubs fetch, backend reuses the US-14 fake generator. |
| VI. Auditing & time | ✅ No writes. N/A. |
| VII. Secrets | ✅ The key stays server-side (US-02); the question is a POST body, never the URL. |
| VIII. Operations & delivery | ✅ Keep-alive interval + streaming headers are config-driven; no migration. CI runs all tiers. |
| IX. Frontend & design system | ✅ Standalone + OnPush + Signals; design tokens (no inline hex); no native dialogs; error codes → PL map; ≥360px; the 404 interceptor unaffected. The chat mounts in the shell (no router yet). |

**No deviations.** No Complexity Tracking entries required.

## Project Structure

### Documentation (this feature)

```text
specs/011-us15-streaming/
├── plan.md · research.md · data-model.md · quickstart.md
├── contracts/chat-stream-client.md   # how the client consumes US-14's SSE (event handling + states)
└── tasks.md                          # (/speckit-tasks)
```

### Source Code (repository root)

```text
src/Web/src/app/
├── core/
│   ├── chat.store.ts                 # signal thread (exchanges); ask(question, scope) [fetch-stream + SSE parse + AbortController]; stop(); retry(); code→PL map
│   └── sse-parser.ts                 # pure incremental SSE parser (bytes/string → {event, data} records) — unit-testable
├── chat/
│   ├── chat.ts / .html / .scss       # the thread view + input + Stop + states + auto-scroll (OnPush, signals)
│   └── scope-selector/               # scope-selector.ts/.html/.scss — All / folder / ready-document (from TreeStore) + active chip
└── app.ts / app.html                 # mount <app-chat/> in the shell (shown when a key is active; the locked notice already exists)

src/RagBook.API/Endpoints/ChatEndpoints.cs   # ADD: periodic keep-alive comment during StreamAnswerAsync (SemaphoreSlim-guarded writes)
src/RagBook/Modules/Chat/RagOptions.cs        # ADD: StreamHeartbeatSeconds (default 15)  [+ appsettings "Rag"]

tests/
├── src/Web/src/app/core/chat.store.spec.ts          # stubbed fetch → incremental append / done / error / interrupted / no-done→error / one-active
├── src/Web/src/app/core/sse-parser.spec.ts          # parser: split events across chunk boundaries
├── src/Web/src/app/chat/chat.spec.ts                # renders thread; ask calls store; Stop; locked/no-basis states
├── src/Web/src/app/chat/scope-selector/*.spec.ts    # options from tree (ready docs only); chip reflects selection
└── tests/RagBook.Api.IntegrationTests/Chat/
    ├── AskQuestionHeartbeatTests.cs                  # tiny StreamHeartbeatSeconds + delaying fake → an SSE comment is emitted
    └── AskQuestionCancellationTests.cs              # abort the client mid-stream → the fake generator observes cancellation (FR-004/AC-5)
# FakeStreamingAnswerGenerator (US-14) EXTENDED: optional per-delta delay + CancellationObserved flag
```

**Structure Decision**: Web modular-monolith. US-15 is a **frontend** slice over US-14's unchanged SSE contract, plus a contained backend heartbeat. The client uses streaming **fetch** (not Angular `HttpClient`) because the endpoint is a `POST` returning a token stream — so `ChatStore` owns a small pure `sse-parser` (unit-tested) and stubs `fetch` in tests. Scope data is reused from `TreeStore`; the key-locked state from `ApiKeyStore`. The chat mounts in the shell (no router yet).

## Complexity Tracking

*No entries — no new project, no migration, no principle deviation. The one non-obvious choice (streaming `fetch` instead of Angular `HttpClient`) is forced by the POST-body + token-stream contract and is isolated behind `ChatStore` + `sse-parser`.*
