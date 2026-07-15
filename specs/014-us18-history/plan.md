# Implementation Plan: Historia rozmowy — wieloturowość + persystencja (US-18)

**Branch**: `014-us18-history` | **Date**: 2026-07-13 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/014-us18-history/spec.md`

## Summary

Make chat conversations **persisted, session-owned, and multi-turn**. Two new session-owned entities —
`Conversation` and `Message` — join the Chat module with a migration; four CQRS slices manage conversations
(create / list / get / delete); the ask flow gains a `conversationId`, persists the **user** message
synchronously at ask start, and persists the **assistant** message via a **durable integration event**
(`ChatTurnCompleted : IExternalEvent`) on stream completion — carrying the final state (answered / no-answer /
interrupted) and the sources as JSON (so citations survive document deletion, consistent with US-16). The prompt
gains the last **N** message pairs (`ChatOptions.HistoryPairs`) as conversational context **alongside** freshly
retrieved passages (retrieval still runs per-question; no query rewriting). The frontend adds a conversation-list
panel + "Nowa rozmowa" + delete-with-confirm, and reworks `ChatStore` from an in-memory thread into a
**conversation-backed** store that loads persisted messages with their states + clickable citations. The SSE
stream contract (event names/order + US-17 `done.state`) is unchanged.

## Technical Context

**Language/Version**: C# (.NET 10) backend; TypeScript / Angular 20 frontend.

**Primary Dependencies**: EF Core 10 + Npgsql (new tables + migration); Wolverine (CQRS dispatch + **durable
outbox** for `IExternalEvent`); existing `IAskQuestionPipeline`/`PromptBuilder`/`ChatEndpoints`; Angular
standalone/OnPush/signals + design tokens; existing session-isolation (`ISessionContext` global query filter) and
auditing interceptors.

**Storage**: PostgreSQL — new `conversations` and `messages` tables (`messages.sources_json jsonb`, `state`,
`role`), FK `messages.conversation_id → conversations.id` **ON DELETE CASCADE**; both session-indexed. Migration
in `RagBook.Infrastructure.Migrations` (never at startup).

**Testing**: xUnit + NSubstitute + FluentAssertions (Domain/Application/Integration); Testcontainers
`pgvector/pgvector:pg17` for Integration; Karma/ChromeHeadless for Angular. No real provider (fake generator).

**Target Platform**: Linux container backend; evergreen browser SPA.

**Project Type**: Web (modular-monolith .NET backend + Angular SPA).

**Performance Goals**: History is bounded to N pairs → the prompt stays O(N + retrieved chunks). List/get are
session-indexed single-table reads. Assistant-message write is off the stream's hot path (durable event).

**Constraints**: No SSE event rename/reorder (persistence is additive); every new entity carries `UserSessionId`
and is covered by the global filter (cross-session → 404); no `window.confirm` (design-system dialog); tokens
only; ≥360px; no magic numbers (`ChatOptions`); migrations not at startup; no real provider in tests.

**Scale/Scope**: Largest slice so far — 2 entities + migration, 4 CQRS slices, an integration event + handler,
ask-flow persistence + history prompt, and a frontend rework. Per-session conversation counts are small (MVP).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **I. Vertical-Slice Modular Monolith** ✅ — `Conversation`/`Message` live in `Modules/Chat/Domain`; slices under
  `Modules/Chat/Features/Conversations/{Create,List,Get,Delete}`; the ask flow extends `Features/AskQuestion`. EF
  configs + migration in Infrastructure/Migrations. No new top-level project; no cross-module reference (folders
  referenced only by scope id, as US-13 already does).
- **II. CQRS + Result Contract** ✅ — Create/Delete are `ICommand`/`ICommand<T>`, List/Get are `IQuery<T>`, each
  returning `Result<T>` → ProblemDetails with new `ChatErrors` codes (`chat.conversation_not_found`, …). The
  assistant-message persistence uses `IExternalEvent` → durable outbox (the constitution's integration-event
  path). The SSE ask endpoint stays the established streaming exception.
- **III. Data Isolation** ✅ — `Conversation` and `Message` implement `ISessionOwned` → the auto-applied global
  query filter covers them; cross-session id → **404**. A Testcontainers test proves the isolation on both tables.
- **IV. Test-First** ✅ — Domain (title truncation, last-N-pairs), Application (handlers, prompt-with-history +
  N-limit, event handler), Integration (persistence, isolation, ask-persists-with-state+sources, load, cascade
  delete, scope-on-deleted-folder), Angular (list/new/switch/load/delete). Red→Green.
- **V. Providers — Resilience + Cache** ✅ — no new provider; `ChatOptions.HistoryPairs` (+ title length) is
  config-driven, no magic numbers. No test hits a real service.
- **VI. Auditing & Time** ✅ — both entities are `IAuditable`, stamped by the existing interceptor; timestamps
  `DateTimeOffset` UTC via `TimeProvider`; actor from `ISessionContext`.
- **VIII. Operations & Delivery** ✅ — migration created in the Migrations project, applied via the init step,
  never at startup.
- **IX. Frontend & Design System** ✅ — standalone/OnPush/signals, `input.required`/`output`, tokens only, the
  conversation-delete confirmation uses the shared design-system dialog (never `window.confirm`), works at ≥360px.

**Result: PASS** — no violations; Complexity Tracking empty. (The durable-event indirection for the assistant
message is the constitution-sanctioned `IExternalEvent` pattern, chosen in clarify for durability — not a
deviation.)

## Project Structure

### Documentation (this feature)

```text
specs/014-us18-history/
├── plan.md, research.md, data-model.md, quickstart.md
├── contracts/conversations.md   # REST + ask changes + the ChatTurnCompleted event
├── checklists/requirements.md
└── tasks.md                     # (/speckit-tasks)
```

### Source Code (repository root)

```text
src/RagBook/Modules/Chat/
├── Domain/
│   ├── Conversation.cs           # ISessionOwned + IAuditable; scope + title; factory + retitle
│   ├── Message.cs                # ISessionOwned + IAuditable; role, content, state, sources (json)
│   ├── MessageRole.cs / MessageState.cs
│   ├── ConversationHistory.cs    # pure: select last N pairs for the prompt
│   └── PromptBuilder.cs          # Build(question, chunks, history) — prepend conversational context
├── ChatOptions.cs                # HistoryPairs (6), TitleMaxChars (60) — bound from "Chat"
└── Features/
    ├── Conversations/{CreateConversation,ListConversations,GetConversation,DeleteConversation}/  # CQRS + handlers
    └── AskQuestion/              # persist user message at start; publish ChatTurnCompleted at end; history in prompt
        └── ChatTurnCompleted.cs  # IExternalEvent + handler persisting the assistant Message

src/RagBook.Infrastructure/SharedContext/Persistence/
├── Configurations/{ConversationConfiguration,MessageConfiguration}.cs
└── RagBookDbContext.cs           # + DbSet<Conversation>, DbSet<Message> (filter auto-applies)
src/RagBook.Infrastructure.Migrations/  # + AddConversationsAndMessages migration

src/RagBook.API/Endpoints/
├── ConversationEndpoints.cs      # GET /api/conversations, GET /{id}, POST, DELETE /{id}
└── ChatEndpoints.cs / ChatContracts.cs  # AskQuestionRequest += ConversationId; guard; persist + publish

src/Web/src/app/
├── core/
│   ├── conversations.store.ts    # signals + HttpClient: list/get/create/delete, activeId
│   └── chat.store.ts             # conversation-backed: load messages→thread; ask carries conversationId
└── chat/
    ├── conversation-list/        # sidebar list + "Nowa rozmowa" + delete (design-system confirm)
    └── chat.* / chat-answer/*    # render loaded messages with saved states + citations (reuse US-16/17)

tests/
├── RagBook.Domain.Tests/Chat/    # ConversationTests (title ≤60), ConversationHistoryTests (last N pairs)
├── RagBook.Application.Tests/Chat/  # Create/List/Get/Delete handlers; PromptBuilder+history+limit; turn-completed handler
├── RagBook.Api.IntegrationTests/Chat/  # persistence, cross-session 404, ask persists state+sources, load, cascade, scope-deleted
└── RagBook.Web (Karma)           # conversation-list, new/switch/load render, delete confirm
```

**Structure Decision**: Conversations belong to the **Chat** vertical slice (the ask flow writes messages; scope
reuses US-13). Two new `ISessionOwned` entities get the isolation filter for free. The assistant message is
written off the stream via `ChatTurnCompleted : IExternalEvent` (durable outbox); the user message is written
synchronously at ask start. `PromptBuilder` gains a bounded history parameter. The frontend splits a
`conversations.store` (HttpClient) from the reworked `chat.store` (keeps `fetch` for SSE).

## Complexity Tracking

*No constitution violations — no entries.*
