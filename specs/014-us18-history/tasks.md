# Tasks: Historia rozmowy — wieloturowość + persystencja (US-18)

**Input**: Design documents from `specs/014-us18-history/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/conversations.md, quickstart.md

**Tests**: Included — Test-First (Constitution §IV). Domain (title/history rules), Application (handlers +
prompt-with-history), Integration (Testcontainers: persistence, isolation, ask, load, cascade), Angular (list /
new / switch / load / delete). **No real Anthropic** — fake generator.

**Organization**: Foundational persistence backbone (entities + migration + options + errors), then the stories.
US1 = multi-turn context + persistence 🎯 MVP; US2 = reload/reopen; US3 = new conversation + list; US4 = bounded
history; US5 = session isolation; Polish = delete + docs.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: parallelizable (different files, no ordering dependency).
- Paths: `src/RagBook/Modules/Chat`, `src/RagBook.Infrastructure`, `src/RagBook.Infrastructure.Migrations`, `src/RagBook.API/Endpoints`, `src/Web/src/app/{core,chat}`, `tests/…`.

---

## Phase 1: Setup

- [X] T001 [P] Add `ChatOptions` (`SectionName="Chat"`, `HistoryPairs=6`, `TitleMaxChars=60`) in `src/RagBook/Modules/Chat/ChatOptions.cs`; register `Configure<ChatOptions>` in `src/RagBook.API/Program.cs`; add a `Chat` section to `appsettings.json`. No magic numbers (§VII).
- [X] T002 [P] Add conversation error codes to `src/RagBook/Modules/Chat/Errors/ChatErrors.cs` — `chat.conversation_not_found` (NotFound → 404), reused for cross-session access.

---

## Phase 2: Foundational — entities, persistence, migration (blocks all stories)

- [X] T003 [P] Domain test (Red): `ConversationTests` — `Start(scope)` yields an empty conversation with that scope; `SetTitleFromFirstQuestion` truncates to `TitleMaxChars` and sets the title **only when empty**; `UpdateScope` records the latest scope — in `tests/RagBook.Domain.Tests/Chat/ConversationTests.cs`.
- [X] T004 Domain (Green): `Conversation` (`ISessionOwned`+`IAuditable`; factory `Start`, `SetTitleFromFirstQuestion`, `UpdateScope`), `Message` (factories `User`/`Assistant`), and `MessageRole`/`MessageState` enums — in `src/RagBook/Modules/Chat/Domain/{Conversation,Message,MessageRole,MessageState}.cs`.
- [X] T005 [P] Domain test + impl: `ConversationHistory.SelectRecent(messages, n)` returns the last `n` `(user, assistant)` pairs in chronological order (fewer if shorter) — `tests/RagBook.Domain.Tests/Chat/ConversationHistoryTests.cs` + `src/RagBook/Modules/Chat/Domain/ConversationHistory.cs`.
- [X] T006 EF mapping: `ConversationConfiguration` + `MessageConfiguration` (snake_case columns, `user_session_id` index, FK `messages.conversation_id → conversations.id` **ON DELETE CASCADE**, `sources_json jsonb`, `scope_type`/`scope_target_id`); add `DbSet<Conversation>`/`DbSet<Message>` to `RagBookDbContext` (global session filter auto-applies) — in `src/RagBook.Infrastructure/SharedContext/Persistence/`.
- [X] T007 Migration `AddConversationsAndMessages` (both tables, session indexes, FK cascade, jsonb) via `dotnet ef migrations add` in `RagBook.Infrastructure.Migrations` — never applied at startup.

**Checkpoint**: entities persist, isolation filter covers them, migration builds.

---

## Phase 3: User Story 1 — Multi-turn context + persistence (Priority: P1) 🎯 MVP

**Goal**: An ask inside a conversation persists both messages and feeds the recent turns + fresh passages to the prompt.

**Independent test**: Create a conversation, ask twice; the second prompt contains the first turn; both turns persist with state + sources.

- [ ] T008 [US1] Application test + impl: `CreateConversationCommandHandler` creates an empty, session-owned conversation (default scope `All`) and returns a summary — `tests/RagBook.Application.Tests/Chat/CreateConversationHandlerTests.cs` + slice `src/RagBook/Modules/Chat/Features/Conversations/CreateConversation/*` + `POST /api/conversations` in `src/RagBook.API/Endpoints/ConversationEndpoints.cs`.
- [ ] T009 [US1] Application test (Red): extend `PromptBuilderTests` — `Build(question, chunks, history)` **prepends** the recent conversation turns before the numbered passages and keeps grounding/`[n]` rules; includes **at most** `HistoryPairs` pairs — in `tests/RagBook.Application.Tests/Chat/PromptBuilderTests.cs`.
- [ ] T010 [US1] `PromptBuilder`/`IPromptBuilder`: add a bounded `history` parameter and prepend it as a short conversational transcript (Green for T009) — in `src/RagBook/Modules/Chat/Domain/PromptBuilder.cs`.
- [ ] T011 [US1] Ask-flow persistence: `AskQuestionRequest += ConversationId` (`ChatContracts.cs`); pre-generation guard in `ChatEndpoints` (conversation must resolve in-session else `404 chat.conversation_not_found`); persist a `user` `Message` at start, set the title if empty, update the conversation's current scope; build the prompt with `ConversationHistory.SelectRecent(...)` — in `src/RagBook.API/Endpoints/ChatEndpoints.cs` (+ pipeline plumbing for history).
- [ ] T012 [US1] `ChatTurnCompleted : IExternalEvent` + handler persisting the assistant `Message` (content, state, `sources_json`); the endpoint publishes it after the stream (and on client-disconnect with `interrupted` + partial text) — in `src/RagBook/Modules/Chat/Features/AskQuestion/ChatTurnCompleted.cs` (+ handler) and `ChatEndpoints.cs`. **Durable messaging is already configured** (`PersistMessagesWithPostgresql`, US-06) — no new infra. **The handler runs outside the request session**, so it MUST set `Message.UserSessionId` (and audit actor) **from the event**, not the ambient `ISessionContext` (A3).
- [ ] T013 [US1] Integration test: create → ask → the `user` + `assistant` messages persist with correct `state` + `sources_json`; a **follow-up** ask's built prompt contains the prior question (capture the `GroundedContext` in the fake generator); an interrupted ask persists `interrupted` + partial text; **a new ask in a conversation whose scope targets a since-deleted folder → `ScopeNotFound`** (FR-009, A1) — in `tests/RagBook.Api.IntegrationTests/Chat/ConversationPersistenceTests.cs` (extend `FakeStreamingAnswerGenerator` to record the last context).

**Checkpoint**: AC-1 — follow-ups remember context; turns persist with state + sources. MVP.

---

## Phase 4: User Story 2 — Reload & reopen (Priority: P1)

**Goal**: Load a past conversation and see every message with its saved state + clickable citations.

**Independent test**: Persist answered/no-answer/interrupted turns; `GET /{id}` returns them ordered; the UI renders states + citations from stored sources.

- [ ] T014 [US2] Application test + impl: `GetConversationQueryHandler` returns the conversation + ordered messages (DTOs: role/content/state/sources), session-filtered (not found → `chat.conversation_not_found`) — tests + slice `Features/Conversations/GetConversation/*` + `GET /api/conversations/{id}`.
- [ ] T015 [US2] Integration test: `GET /{id}` returns persisted messages ordered by time with preserved `state` (no_answer/interrupted) and `sources_json`; a conversation whose scope targets a **deleted folder** still loads — in `tests/RagBook.Api.IntegrationTests/Chat/ConversationPersistenceTests.cs`.
- [ ] T016 [US2] Frontend: `conversations.store.ts` `get(id)` (HttpClient) + rework `chat.store.ts` to load `Message[]` → `ChatExchange[]` (map role/content/`state`→status, `sources`→`Source[]`); reuse `chat-answer` to render; `ask` carries the active `conversationId` — in `src/Web/src/app/core/{conversations.store,chat.store}.ts` + specs asserting loaded states + clickable citations render **from the stored `sources` (not live chunks)** — so they survive document deletion (SC-004, A2).

**Checkpoint**: AC-3 — history reloads with states + citations (from snippets, survives deletion).

---

## Phase 5: User Story 3 — New conversation + list (Priority: P1)

**Goal**: List conversations, switch between them, and start a fresh one.

**Independent test**: "Nowa rozmowa" creates an empty conversation (scope Wszystkie); the previous stays listed; selecting one loads it.

- [ ] T017 [US3] Application test + impl: `ListConversationsQueryHandler` returns the session's conversations, most-recent first (summaries) — tests + slice `Features/Conversations/ListConversations/*` + `GET /api/conversations`.
- [ ] T018 [US3] Frontend: `conversations.store` `list()` + `create()` + `activeId`; a `conversation-list` component (sidebar) with "Nowa rozmowa" (create → empty, default scope Wszystkie, activate + clear thread) and switching (loads via T016); wire into the chat page — in `src/Web/src/app/chat/conversation-list/*` + specs.

**Checkpoint**: AC-2 — new conversation clears context, previous stays listed.

---

## Phase 6: User Story 4 — Bounded history (Priority: P2)

**Goal**: Only the last N pairs feed the prompt regardless of conversation length.

**Independent test**: A conversation longer than N pairs → the prompt includes only the last N.

- [ ] T019 [US4] Test: extend `ConversationHistoryTests`/`PromptBuilderTests` with a **>N pairs** case asserting only the last `HistoryPairs` pairs reach the prompt; document the `HistoryPairs` default — in `tests/RagBook.Domain.Tests`/`tests/RagBook.Application.Tests`.

**Checkpoint**: AC-4 — history is bounded; older turns are UI-only.

---

## Phase 7: User Story 5 — Session isolation (Priority: P2)

**Goal**: One session cannot see another's conversations.

**Independent test**: Create in session A; `GET`/`DELETE` `/{id}` as session B → 404.

- [ ] T020 [US5] Integration test: a conversation created in session A → `GET /{id}` and `DELETE /{id}` as session B both return **404** (no existence disclosure), on both tables — in `tests/RagBook.Api.IntegrationTests/Chat/ConversationIsolationTests.cs`.

**Checkpoint**: AC-5 — cross-session access is 404 (consistent with US-01).

---

## Phase 8: Polish

- [ ] T021 [P] `DeleteConversation`: slice (`ICommand`, hard delete — FK cascade removes messages) + `DELETE /api/conversations/{id}` + integration test (cascade removes messages; cross-session → 404) + frontend delete behind the **design-system confirm dialog** (never `window.confirm`) + spec — `Features/Conversations/DeleteConversation/*`, `ConversationEndpoints.cs`, `conversation-list` + `conversations.store`.
- [ ] T022 [P] Docs: README **"Historia rozmowy (US-18)"** — persisted multi-turn conversations, per-question retrieval + last-`HistoryPairs` context, the retrieval trade-off (purely-referential follow-ups; future work: condensing question), `sources_json` survives document deletion, session isolation; AGENTS.md durable notes (`Conversation`/`Message` entities + isolation filter; `ChatTurnCompleted : IExternalEvent` outbox persistence; user message sync at ask start; `ChatOptions.HistoryPairs`; ask `+= conversationId`; `ChatStore` conversation-backed; no SSE contract change).
- [ ] T023 Full green run — `npm test` in `src/Web` and `dotnet test` (Domain + Application + Testcontainers Integration; Docker up; migration applied) — then the critical-analysis pass on the diff before opening the PR (repo memory: critical-analysis-before-pr). Then PR to master.

---

## Dependencies & execution order

- **Setup (T001–T002)** + **Foundational (T003–T007)** block the stories.
- **US1 (T008–T013)** is the MVP: create + persistence + history prompt + event. **US2 (T014–T016)** adds get +
  load-render; **US3 (T017–T018)** adds list + new-conversation UI; **US4 (T019)** pins the bound; **US5 (T020)**
  the isolation. Polish (T021–T023): delete + docs + green.
- Within a story, tests precede implementation; `[P]` = different files.

## MVP scope

**US1 (T001–T013)** delivers the demonstrable increment: a persisted conversation where a follow-up remembers the
prior turn and every turn is saved with its state + citations. US2–US5 add reload/reopen, the conversation list +
"Nowa rozmowa", the history bound, and session isolation; Polish adds delete + docs.
