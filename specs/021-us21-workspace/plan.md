# Implementation Plan: Notebook-style workspace redesign (US-21)

**Branch**: `021-us21-workspace` | **Date**: 2026-07-18 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/021-us21-workspace/spec.md`

## Summary

Reshape the app into a NotebookLM-style workspace, delivered in **three staged PRs** (each green + reviewed):

- **Stage 1 — Shell + onboarding (frontend only)**: a config-first gate (API key **or** demo), then a 4-column CSS
  grid — **conversations (collapsible) | sources | chat | Studio** — driven by one shared active-conversation
  signal. Pulls `ConversationList` out of `Chat` into its own column, moves `DocumentUpload`+`DocumentTree`+`QuotaBar`
  into a sources column, keeps `Chat` in the middle, and adds a Studio column (placeholder tiles for now).
- **Stage 2 — Per-conversation sources (domain change + migration)**: `Folder` and `Document` gain a `ConversationId`;
  a migration adds `conversation_id` to `folders` + `documents` (+ cascade on conversation delete); upload pins to
  the active conversation; the tree read + retriever are scoped to it; folders become per-conversation. Legacy
  (null-conversation) rows are hidden; demo docs stay global read-only.
- **Stage 3 — Studio summary**: `POST /api/conversations/{id}/summary` builds a `GroundedContext` from the
  conversation's sources and reuses `IAnswerGenerator` (BYOK/demo key) to produce a summary; the Studio "Podsumowanie"
  tile renders it with empty/disabled states.

Session isolation, quota (session-wide), BYOK/demo, folders, and the RAG pipeline are reused. This plan covers all
three stages; each ships as its own PR.

## Technical Context

**Language/Version**: C# (.NET 10) backend; TypeScript / Angular 20 frontend.

**Primary Dependencies**: conversations (US-18: `Conversation`, `IConversationRepository`, `conversations.store`),
documents/folders/tree (US-04/07/09/10/11/12), retrieval (US-13 `ScopedRetriever`), generation (US-14
`IAnswerGenerator` + `IPromptBuilder`), BYOK/demo keys (US-02/03), the app shell (`app.ts`/`app.html`).

**Storage**: PostgreSQL — **Stage 2 migration** adds nullable `conversation_id` to `folders` + `documents` (+ index;
FK to `conversations` with cascade). Stage 1 + Stage 3 add no schema.

**Testing**: Angular Karma (shell composition, onboarding gate, collapsible columns, shared active state, Studio
tiles + summary rendering); Application (upload pins conversation; retriever conversation predicate; summary handler
grounds on the conversation's sources); Testcontainers integration (upload→pinned; tree scoped to conversation; ask
grounded per conversation; conversation delete cascades sources+chunks; summary endpoint). Red→Green.

**Target Platform**: containerised API + nginx SPA (US-20).

**Project Type**: Web (modular-monolith .NET + Angular SPA).

**Constraints**: migration off the app-start path (§VIII); session isolation preserved (conversation + its sources
belong to the session; cross-session id → 404); quota stays session-wide; design tokens, ≥360px, collapsible panels
keep the active selection + in-flight chat; demo global read-only; no real auth.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **I. Vertical-Slice Modular Monolith** ✅ — Stage 2 threads `ConversationId` through the Documents/Folders/Chat
  slices + Infrastructure; Stage 3 adds a `Conversations/Summarize` slice. No cross-module reach-in (the retriever +
  tree already live in Infrastructure; the conversation link is a column + a scope param).
- **II. CQRS + Result Contract** ✅ — new/changed commands + queries return `Result<T>` → ProblemDetails; the summary
  is a new endpoint following the SSE/ask pattern or a single `Result`.
- **III. Data Isolation** ✅ — conversations + their sources stay `ISessionOwned` (session-scoped global filter); the
  conversation link is an **additional** predicate, never a bypass; a cross-session conversation id reads as 404.
- **IV. Test-First** ✅ — each stage red→green: Karma (shell/onboarding/Studio), Application (pin/retrieve/summary),
  Integration (upload-pinned, per-conversation tree/ask, cascade delete, summary).
- **V. Providers** ✅ — the summary reuses the resilient Anthropic client + BYOK/demo key path; no new provider.
- **VI/VII** ✅ — no time/secret changes. **VIII. Ops** ✅ — the conversation-link migration is a **separate step**
  (the US-20 `migrate` service / bundle), never at app startup; cascade delete is one FK.
- **IX. Frontend & Design System** ✅ — 4-column grid with collapsible panels, design tokens, ≥360px, keyboard-usable
  tiles, no native dialogs; the onboarding gate + Studio empty states are token-styled.

**Result: PASS** — no violations; Complexity Tracking empty. The staged delivery keeps each PR small and green.

## Project Structure

### Documentation (this feature)

```text
specs/021-us21-workspace/
├── plan.md, research.md, data-model.md, quickstart.md
├── contracts/workspace.md
├── checklists/requirements.md
└── tasks.md   (/speckit-tasks)
```

### Source Code (repository root)

```text
# Stage 1 — shell + onboarding (frontend)
src/Web/src/app/
├── core/workspace.store.ts            # shared active-conversation state (wraps ConversationsStore); column collapse state
├── app.ts / app.html / app.scss       # 4-column grid; onboarding gate (config-first); collapsible conversations/sources
├── chat/chat.ts                       # ConversationList pulled out to the shell (chat consumes the shared active id)
├── workspace/onboarding/*             # config-first step (ApiKeySettings + "continue in demo")
└── workspace/studio/*                 # Studio column (Podsumowanie tile + upcoming placeholders)

# Stage 2 — per-conversation sources (domain + migration)
src/RagBook/Modules/Documents/Domain/Document.cs        # + Guid? ConversationId (CreateUpload)
src/RagBook/Modules/Folders/Domain/Folder.cs            # + Guid? ConversationId
src/RagBook.Infrastructure/SharedContext/Persistence/Configurations/{Document,Folder}Configuration.cs  # conversation_id + FK cascade
src/RagBook.Infrastructure.Migrations/Migrations/*_AddConversationScopedSources.cs   # migration (both tables + cascade)
src/RagBook/Modules/Documents/Features/UploadDocument/*  # + ConversationId
src/RagBook.API/Endpoints/DocumentEndpoints.cs           # read form["conversationId"]
src/RagBook.Infrastructure/SharedContext/Persistence/TreeReader.cs   # filter by active conversation
src/RagBook.Infrastructure/SharedContext/Retrieval/ScopedRetriever.cs  # + AND d.conversation_id = @conv
src/RagBook/Modules/Chat/Features/AskQuestion/*          # thread conversationId → retriever
src/Web/src/app/core/{tree,document-upload}.store.ts     # pass active conversationId

# Stage 3 — Studio summary
src/RagBook/Modules/Chat/Features/Conversations/Summarize/*   # command + handler (retrieve → GroundedContext → generate)
src/RagBook.API/Endpoints/ConversationEndpoints.cs           # POST /api/conversations/{id}/summary
src/Web/src/app/workspace/studio/*                           # Podsumowanie tile calls it
```

**Structure Decision**: Stage 1 restructures the shell + adds a `workspace.store` (one active-conversation source of
truth) with zero backend change — the fast visible win. Stage 2 adds `ConversationId` to `Folder`+`Document` (one
migration, cascade on conversation delete) and threads it through upload → tree → retrieval. Stage 3 adds a
`Conversations/Summarize` slice reusing generation. Each stage is its own PR.

## Complexity Tracking

*No constitution violations — no entries.*
