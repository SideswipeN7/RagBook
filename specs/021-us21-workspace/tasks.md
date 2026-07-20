# Tasks: Notebook-style workspace redesign (US-21)

**Input**: Design documents from `specs/021-us21-workspace/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/workspace.md, quickstart.md

**Tests**: REQUIRED (constitution §IV; all 4 tiers green before any PR).

**Delivery**: **three staged PRs** — Stage 1 (shell+onboarding, frontend), Stage 2 (per-conversation sources,
domain+migration), Stage 3 (Studio summary). Each stage is its own PR + critical review + CI.

## Format: `[ID] [P?] Description`

---

## Stage 1 — 4-column shell + onboarding (PR 1, frontend only) 🎯 MVP

- [x] T001 Branch `fm/us21-workspace` off master (US-20 merged `f562bfe`); no backend change in this stage.
- [x] T002 [P] Karma `core/workspace.store.spec.ts` (FAIL first): `activeConversationId` set/read; `conversationsCollapsed`/`sourcesCollapsed` toggle; `configured` computed (key active OR demo available OR continued read-only).
- [x] T003 `core/workspace.store.ts`: shared `activeConversationId` signal + column-collapse signals + `configured` computed + `continueReadonly()`; wraps `ConversationsStore` (single source of truth for the active id).
- [x] T004 [P] `workspace/onboarding/*` — a config-first step: `ApiKeySettings` + a "Kontynuuj w trybie demo / bez klucza" action; Karma: renders before the workspace; choosing demo/continue sets `configured`.
- [x] T005 [P] `workspace/studio/*` — the Studio column with a "Podsumowanie" tile (disabled placeholder in Stage 1) + `wkrótce` tiles; Karma: renders tiles.
- [x] T006 `app.ts`/`app.html`/`app.scss`: a 4-column CSS grid (conversations collapsible | sources | chat | Studio) gated by `workspace.configured`; `ConversationList` in the conversations column; `QuotaBar`+`DocumentUpload`+`DocumentTree` in the sources column; `Chat` central (reads the shared active id); `Studio` fourth. Collapse toggles keep the active selection. Design tokens, ≥360px.
- [x] T007 Rewire `chat.ts` to read/write the active conversation via `workspace.store` (drop its private `ConversationList` ownership); update `chat.spec.ts` + `app.spec.ts`.
- [x] T008 Run Angular Karma green + confirm the running compose stack still serves the new shell; critical review; **PR 1**.

---

## Stage 2 — Per-conversation sources (PR 2, domain + migration)

- [ ] T009 [P] `Document` + `Guid? ConversationId` (via `CreateUpload`); `Folder` + `Guid? ConversationId` (on create).
- [ ] T010 [P] `{Document,Folder}Configuration.cs`: `conversation_id` column + index + FK → `conversations(id)` `ON DELETE CASCADE`.
- [ ] T011 Migration `*_AddConversationScopedSources` in `RagBook.Infrastructure.Migrations` (both tables + indexes + cascade FK). Applied via the US-20 `migrate` step, never at app startup (§VIII).
- [ ] T012 `UploadDocumentCommand` + `POST /api/documents` (read `form["conversationId"]`) + `CreateFolderCommand` + `POST /api/folders` (body `conversationId`) → pin to the conversation. Application tests (upload/create pins).
- [ ] T013 `TreeReader` + `GetTreeQuery` + `GET /api/tree?conversationId=` → return the conversation's folders/documents (demo global). Integration test: upload in A → in A's tree, not B's.
- [ ] T014 `ScopedRetriever.FilterClause` + `AddScopeParameters` `AND d.conversation_id = @conv`; thread `conversationId` `ChatEndpoints → AskQuestionPipeline.PrepareAsync → IScopedRetriever.RetrieveAsync`. Application + integration: ask in A grounds only on A's sources.
- [ ] T015 [P] Integration test: `DELETE /api/conversations/{id}` cascades its folders + documents + chunks; quota drops; a second conversation untouched.
- [ ] T016 Frontend `tree.store`/`document-upload.store`: pass `workspace.activeConversationId` on refresh/upload; sources column shows the active conversation's tree; Karma. Legacy/demo handling: demo section still read-only.
- [ ] T017 Run all 4 tiers green + `docker compose` migrate/build; critical review; **PR 2**.

---

## Stage 3 — Studio summary (PR 3)

- [ ] T018 [P] Application test `SummarizeConversationHandler`: grounds on the conversation's ready sources; no sources → empty result; foreign conversation → not-found. (FAIL first.)
- [ ] T019 `Modules/Chat/Features/Conversations/Summarize/*` — command + handler: load conversation (session-scoped), retrieve its ready sources, build `GroundedContext` (summarise prompt), reuse `IAnswerGenerator` (BYOK/demo key).
- [ ] T020 `POST /api/conversations/{id}/summary` in `ConversationEndpoints.cs` → the summary (single `Result<string>` or SSE); key/demo guards reused. Integration test.
- [ ] T021 [P] Frontend `workspace/studio` "Podsumowanie" tile → `POST .../summary`; render the summary; empty/disabled state with no sources; Karma.
- [ ] T022 Run all 4 tiers green; critical review; **PR 3** (completes US-21).

---

## Dependencies & Execution Order

- **Stage 1 (T001–T008)** is independent + frontend-only → ships first (fast visible redesign).
- **Stage 2 (T009–T017)** depends on Stage 1's `workspace.store` (active id) + the domain/migration. T009/T010 → T011 (migration) → T012/T013/T014 (upload/tree/retrieval) → T015 (cascade) → T016 (frontend).
- **Stage 3 (T018–T022)** depends on Stage 2 (conversation-scoped sources to summarise).

## Implementation Strategy

Ship the **visible redesign first** (Stage 1: 4-column shell + onboarding, reusing every existing component, zero
backend risk). Then the **model change** (Stage 2: per-conversation folders+sources via one migration + cascade,
threaded through upload/tree/retrieval). Then the **Studio summary** (Stage 3: a new slice reusing generation). Each
stage: red→green across the affected tiers, critical diff review, PR + CI.
