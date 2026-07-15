# Phase 1 Data Model — US-18

Two new session-owned, auditable entities + a migration. Both implement `ISessionOwned` (→ global query filter,
cross-session 404) and `IAuditable` (→ central interceptor stamping).

## Entity: Conversation → table `conversations`

| Field | Type | Notes |
|---|---|---|
| Id | `Guid` (PK) | `id` |
| UserSessionId | `Guid` | `user_session_id`, indexed (`ix_conversations_user_session_id`), stamped on insert |
| ScopeType | `string` | `scope_type` — `all` \| `folder` \| `document` (from `ChatScope`) |
| ScopeTargetId | `Guid?` | `scope_target_id` — folder/document id; null for `all`. Current/last-used (changeable per ask) |
| Title | `string` | `title` — first question truncated to `ChatOptions.TitleMaxChars` (60); empty until first ask |
| CreatedAt / CreatedBy / ModifiedAt / ModifiedBy | audit | stamped centrally |

- Factory `Conversation.Start(ChatScope scope)` → empty conversation (default scope `All` from the caller).
- `SetTitleFromFirstQuestion(question, maxChars)` — sets the title only when still empty (idempotent per first ask).
- `UpdateScope(ChatScope)` — records the latest ask's scope.
- Has many `Message` (ordered by `CreatedAt`). Delete cascades to messages (DB FK `ON DELETE CASCADE`).

## Entity: Message → table `messages`

| Field | Type | Notes |
|---|---|---|
| Id | `Guid` (PK) | `id` |
| ConversationId | `Guid` (FK) | `conversation_id` → `conversations.id` **ON DELETE CASCADE**; indexed |
| UserSessionId | `Guid` | `user_session_id`, indexed; stamped on insert (defense-in-depth + direct queries) |
| Role | `string` | `role` — `user` \| `assistant` |
| Content | `string` | `content` — question text, or the assistant answer (partial if interrupted) |
| State | `string?` | `state` — assistant only: `answered` \| `no_answer` \| `interrupted`; null for user |
| SourcesJson | `string?` (jsonb) | `sources_json` — assistant `SourceDto[]` (US-16 shape); null/empty otherwise |
| CreatedAt / CreatedBy / ModifiedAt / ModifiedBy | audit | stamped centrally; ordering key = `created_at` |

- Factories `Message.User(conversationId, content)` and `Message.Assistant(conversationId, content, state, sourcesJson)`.
- `role`/`state` persisted as strings (stable, readable); enums `MessageRole`/`MessageState` in Domain.

## Relationships & migration

- `conversations 1—* messages`, FK cascade delete.
- Migration `AddConversationsAndMessages` in `RagBook.Infrastructure.Migrations`: both tables, session indexes,
  the FK + cascade, `sources_json jsonb`. No pgvector involvement.
- `RagBookDbContext` gains `DbSet<Conversation>` + `DbSet<Message>`; the global session filter auto-applies (both
  are `ISessionOwned`).

## Reused wire/state shapes (unchanged)

- `SourceDto { number, documentId, fileName, pageNumber, text, chunkId }` (US-16) — stored verbatim in
  `sources_json`, returned on load, mapped to the frontend `Source[]`.
- `done.state ∈ { answered, no_answer }` (US-17) + `interrupted` (US-15 abort) → the persisted `Message.State`.
- `ChatScope = All | Folder(id) | Document(id)` (US-13) → `scope_type` + `scope_target_id`.

## History window

`ConversationHistory.SelectRecent(messages, n)` → the last `n` `(user, assistant)` pairs in chronological order,
for the prompt. `n = ChatOptions.HistoryPairs` (default 6). Older messages are UI-only.

## API DTOs (no new persisted entities)

| DTO | Shape |
|---|---|
| `ConversationSummaryDto` | `id, title, scopeType, scopeTargetId, createdAt` (list) |
| `ConversationDetailDto` | summary + `messages: MessageDto[]` |
| `MessageDto` | `id, role, content, state, sources: SourceDto[]?, createdAt` |
| `CreateConversationRequest` | `scope: ScopeDto` (defaults to `all`) → returns `ConversationSummaryDto` |
| `AskQuestionRequest` (extended) | `+ conversationId: Guid` (question + scope unchanged) |
