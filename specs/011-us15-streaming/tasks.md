# Tasks: Streaming odpowiedzi — chat UI + SSE hardening (US-15)

**Input**: Design documents from `specs/011-us15-streaming/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/chat-stream-client.md, quickstart.md

**Tests**: Included — Test-First. Frontend behaviors land via failing Karma tests that **stub the global `fetch`**
(HttpTestingController does not intercept fetch); backend hardening via failing Testcontainers tests reusing
US-14's fake generator. No test hits Anthropic (§V).

**Organization**: Mostly frontend over US-14's unchanged SSE contract, plus a contained backend heartbeat.
One Setup phase (config), one Foundational phase (SSE parser + store scaffold + extended fake), then the
stories: US1 = live streaming (AC-1/4) 🎯 MVP, US2 = stop + cancel (AC-2/5), US3 = error (AC-3), US4 = scope
selector (AC-6), US5 = locked/no-basis (AC-7), plus heartbeat hardening (FR-010).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel (different files, no incomplete dependency).
- Paths: `src/Web/src/app/{core,chat}`, `src/RagBook.API`, `tests/…`.

---

## Phase 1: Setup

- [X] T001 [P] Add `RagOptions.StreamHeartbeatSeconds` (default 15) in `src/RagBook/Modules/Chat/RagOptions.cs`; add to the `"Rag"` section in `src/RagBook.API/appsettings.json`.

**Checkpoint**: builds; the heartbeat interval binds.

---

## Phase 2: Foundational (parser + store scaffold — BLOCK the stories)

- [X] T002 [P] Web test (Red): `sse-parser.spec.ts` — feeding SSE byte chunks yields `{event,data}` records in order, **an event split across two chunks** is assembled, and `:` comment lines are ignored — in `src/Web/src/app/core/sse-parser.spec.ts`.
- [X] T003 Implement pure `sse-parser.ts` (buffer + split on blank line; extract `event:`/`data:`; skip comments) in `src/Web/src/app/core/sse-parser.ts` (Green for T002).
- [X] T004 [P] Extend `FakeStreamingAnswerGenerator` (US-14, `tests/RagBook.Api.IntegrationTests/Chat/Fakes/`): optional `DelayPerDelta` (await with the ct between deltas) + a `CancellationObserved` flag set when it sees `OperationCanceledException`.
- [X] T005 [P] `ChatStore` scaffold: `ChatExchange` / `ChatScopeSelection` / `Source` types, the `thread` signal, the code→PL `ERROR_MESSAGES` record, and computed helpers (`activeExchange`, `isStreaming`) in `src/Web/src/app/core/chat.store.ts` (no `ask` yet).

**Checkpoint**: parser green; store types compile; the fake can delay + record cancellation.

---

## Phase 3: User Story 1 — Live streamed answer (Priority: P1) 🎯 MVP

**Goal**: Asking streams the answer token-by-token: `sources` → incremental `token`s → `done`.

**Independent test**: A stubbed fetch scripts `sources` + 2×`token` + `done`; `ask()` grows the answer incrementally and completes.

- [X] T006 [P] [US1] Web test (Red): `chat.store.spec.ts` — stub `window.fetch` to return a `Response` with a scripted SSE `ReadableStream`; `ask()` → `sources` is set **before** the answer starts growing (order — A3), the exchange's `answer` updates **multiple times** (incremental), and it ends `complete` with `groundsFound:true` — in `src/Web/src/app/core/chat.store.spec.ts`.
- [X] T007 [US1] Implement `ChatStore.ask(question, scope)`: abort any active stream first (FR-008), push a `streaming` exchange, `fetch('/api/chat/ask', {method:'POST', credentials:'same-origin', headers, body, signal})` — **the session cookie MUST ride the request (C1)**, else the ask hits a fresh session → 401 — read `response.body` via the `sse-parser`, per event append `token.text` / set `sources` / `done`→`complete`; register in `chat.store.ts` (Green for T006). Assert in the spec that the stubbed fetch was called with `credentials: 'same-origin'`.
- [X] T008 [US1] `Chat` component (`src/Web/src/app/chat/chat.ts|html|scss`, OnPush/signals/tokens): renders the multi-turn thread + a question input + Send; auto-scroll with detach — extract the stick/detach decision into a **pure helper** `shouldStickToBottom(scrollTop, clientHeight, scrollHeight)` unit-tested in isolation (A2; the DOM scroll effect itself is not asserted). Mount `<app-chat/>` in the shell (`app.ts`/`app.html`) and **remove the now-redundant standalone chat-locked notice from the shell** — the chat owns the locked state (A1; T014). Component spec: entering a question + Send calls `ChatStore.ask`; the thread renders exchanges.

**Checkpoint**: AC-1/AC-4 — a grounded answer streams incrementally in the UI. MVP.

---

## Phase 4: User Story 2 — Stop + cancellation (Priority: P1)

**Goal**: Stop aborts the stream (partial kept, interrupted) and the backend cancels generation.

**Independent test**: Stop during a slow stubbed stream → `interrupted`, partial answer kept; and a client abort mid-stream → the fake generator observes cancellation.

- [X] T009 [US2] Web test + impl: `ChatStore.stop()` aborts the active controller → the exchange becomes `interrupted`, the partial `answer` is kept, and no later `token` is applied; a second `ask` while streaming aborts the first (one active — FR-008/SC-005). Chat component shows a **Stop** button while streaming — in `chat.store.spec.ts` + `chat` component.
- [X] T010 [US2] Integration test (Red→Green): `AskQuestionCancellationTests` — with a delaying `FakeStreamingAnswerGenerator`, start `POST /api/chat/ask`, then abort the client request mid-stream; assert `Generator.CancellationObserved == true` (FR-004/AC-5) — in `tests/RagBook.Api.IntegrationTests/Chat/`.

**Checkpoint**: AC-2/AC-5 — Stop halts + backend cancels; no wasted tokens.

---

## Phase 5: User Story 3 — Mid-stream error is clear (Priority: P1)

**Goal**: An `error` event, a non-2xx ProblemDetails, or a stream without `done` → visible message + Try again.

**Independent test**: Stub streams for each failure → the exchange is `error` with a mapped PL message + a Try-again action re-runs the ask.

- [X] T011 [US3] Web test + impl: an `error` SSE event → `error` + `ERROR_MESSAGES[code]`; a non-2xx ProblemDetails response → `error` with its `.code`; a stream that ends **without** `done` → `error` ("interrupted"); `retry(exchange)` re-runs the same question+scope — in `chat.store.spec.ts` + the `chat` component (partial text + error message + **Try again** button).

**Checkpoint**: AC-3 — failures are always explained with retry, never a silent cut.

---

## Phase 6: User Story 4 — Scope selector (Priority: P1)

**Goal**: Pick All / folder / ready document from the tree; the chip reflects it and it is sent with the question.

**Independent test**: The selector lists All + folders + **ready** documents; choosing one updates the chip and the next ask carries that scope.

- [X] T012 [P] [US4] `scope-selector` component (`src/Web/src/app/chat/scope-selector/`, from `TreeStore`) + spec: options = All + each folder + each document with `status==='Ready'` (processing/failed excluded); selecting emits `{type,targetId,label}`.
- [X] T013 [US4] Wire the selector into `Chat`: an **active-scope chip** above the input (default "Wszystkie dokumenty"); `ask` sends the selected scope; component spec asserts the chosen scope reaches `ChatStore.ask`.

**Checkpoint**: AC-6 — the question is scoped from the UI.

---

## Phase 7: User Story 5 — Locked / no-basis states (Priority: P2)

**Goal**: No key → input disabled + settings link; `done{groundsFound:false}` → neutral no-basis note.

**Independent test**: With `chatLocked`, the input is disabled + links to settings; a `groundsFound:false` stream shows the neutral note, no answer text.

- [X] T014 [US5] Chat component: bind the input's disabled state + settings link to `ApiKeyStore.chatLocked` (US-02); render a neutral "brak podstaw w wybranym zakresie" note when an exchange completes with `groundsFound:false` (no answer text; full refusal UX is US-17) — component spec for both.

**Checkpoint**: AC-7 — honest locked + no-basis states.

---

## Phase 8: Backend heartbeat + polish

- [X] T015 [P] Backend hardening: emit a keep-alive SSE comment (`: keep-alive\n\n`) every `Rag:StreamHeartbeatSeconds` during `StreamAnswerAsync`, serializing all response writes with a `SemaphoreSlim` so heartbeat + events never interleave — in `src/RagBook.API/Endpoints/ChatEndpoints.cs`. Integration test `AskQuestionHeartbeatTests`: a tiny `StreamHeartbeatSeconds` (via test host) + a delaying fake → a comment line appears in the body before `done` (FR-010/SC-006).
- [X] T016 [P] Docs: add a **"Streaming SSE (US-15)"** section to `README.md` (client consumes US-14's `sources`→`token`→`done`/`error` via streaming fetch; Stop=AbortController→server cancel; keep-alive; scope selector; error→PL map; multi-turn in-memory thread) and record durable notes in `AGENTS.md` (chat UI = `ChatStore` fetch-stream + `sse-parser`; NOT `HttpClient`/`EventSource`; heartbeat + semaphore; fake generator extended).
- [X] T017 Full green run: `npm test` in `src/Web` and `dotnet test tests/RagBook.Api.IntegrationTests`; then the critical-analysis pass on the diff before opening the PR (repo memory: critical-analysis-before-pr). If Smart App Control blocks local test hosts, push and let CI be the green gate.

---

## Dependencies & execution order

- **Setup (T001)** → **Foundational (T002–T005)** block the stories.
- **US1 (T006–T008)** builds the store `ask` + component (MVP). **US2 (T009–T010)** adds stop + the backend cancellation proof; **US3 (T011)** error handling; **US4 (T012–T013)** the scope selector; **US5 (T014)** locked/no-basis. **Heartbeat (T015)** is independent backend hardening.
- Within a story, tests precede implementation; `[P]` = different files.
- Polish (T016–T017) after the stories are green.

## MVP scope

**US1 (T001–T008)** yields the demonstrable increment: a scoped question streams a grounded answer token-by-token into a multi-turn chat thread. US2–US5 add Stop+cancellation, error+retry, the scope selector, and the locked/no-basis states; the heartbeat hardens long streams behind a proxy.
