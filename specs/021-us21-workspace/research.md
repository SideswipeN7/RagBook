# Phase 0 Research — US-21 Workspace redesign

## D1 — Folders + sources per conversation (clarify Q1)

**Decision**: Both `Folder` and `Document` gain a nullable `ConversationId`. Each conversation owns its folder tree +
sources; a new conversation starts empty. Retrieval's `all` scope = "this conversation's ready sources"; the folder
scope stays `path LIKE` **plus** `conversation_id = @conv`. One migration adds `conversation_id` to `folders` +
`documents` (index + FK to `conversations`, cascade delete).

**Rationale**: The most coherent NotebookLM model that keeps folders + drag-drop + multi-select (all now scoped to a
conversation). Isolation is unchanged — rows stay `ISessionOwned`; the conversation id is an **extra** predicate.

**Alternatives rejected**: session-wide folders with a conversation-filtered doc list (a folder could differ per
conversation — confusing); a flat source list (drops the folder tree the user wants).

## D2 — Legacy / demo / delete (clarify Q2)

**Decision**: Legacy (null-`conversation_id`) folders/documents are **hidden** from the per-conversation view (clean
start — no production data). Demo documents stay **global, read-only** (`Origin == Demo`, keyless demo works in any
conversation, shown as a separate read-only section). Deleting a conversation **cascades** its folders → documents →
chunks (FK cascade + best-effort blob per existing delete pattern).

**Rationale**: A portfolio project with no real data → a clean start is simplest and correct; cascade matches a
NotebookLM notebook.

## D3 — Shared active-conversation state (Stage 1)

**Decision**: A `workspace.store.ts` is the single source of truth for the active conversation id + the column
collapse flags. `ConversationsStore.activeId` and `ChatStore.activeConversationId` are reconciled to read from it
(the `Chat` component no longer owns selection). The conversations, sources, chat, and Studio columns all read
`workspace.activeConversationId`. Selecting a conversation updates it once; every column reacts.

**Rationale**: The four columns need one active id; today it's duplicated in two stores + orchestrated by `Chat`.
Centralising avoids drift when the list becomes its own column.

## D4 — 4-column shell + onboarding (Stage 1)

**Decision**: `app.html` becomes a CSS-grid workspace: `conversations (collapsible) | sources | chat | Studio`. An
**onboarding gate** renders `ApiKeySettings` + a "Kontynuuj w trybie demo" action first; the workspace mounts once
`apiKey.status() === 'active' || demo.available()` (or the user explicitly continues read-only). `ConversationList`
moves from inside `Chat` to the conversations column; `QuotaBar`+`DocumentUpload`+`DocumentTree` compose the sources
column; `Chat` stays central; a new `Studio` component is the fourth column. Panels collapse via `workspace.store`
flags without losing the active id or an in-flight stream.

**Rationale**: Reuses every existing component in a new layout — the visible NotebookLM redesign with zero backend
change, shippable as PR 1.

## D5 — Threading conversationId to upload + retrieval (Stage 2)

**Decision**: `UploadDocumentCommand` + the `POST /api/documents` endpoint gain `ConversationId` (read
`form["conversationId"]` like `folderId`); `Document.CreateUpload` stores it. `TreeReader` filters documents +
folders by the active conversation (via a `?conversationId=` query on `GET /api/tree` / a `GetTreeQuery(convId)`).
`ScopedRetriever.FilterClause` appends `AND d.conversation_id = @conv` for the non-demo branch; the id is threaded
`ChatEndpoints → AskQuestionPipeline.PrepareAsync → IScopedRetriever.RetrieveAsync`. Frontend `document-upload.store`
+ `tree.store` pass `workspace.activeConversationId`.

**Rationale**: One additive predicate per read path; the endpoint already knows the conversation id (the ask loads
the conversation), so threading it is mechanical. Demo retrieval is unchanged (global).

## D6 — Studio summary (Stage 3)

**Decision**: A `Modules/Chat/Features/Conversations/Summarize` slice: load the conversation (session-scoped → 404 if
foreign), retrieve its ready sources (a broad retrieval / all chunks of the conversation), build a `GroundedContext`
with a "summarise these sources" prompt, and reuse `IAnswerGenerator` on the BYOK/demo key. `POST
/api/conversations/{id}/summary` returns the summary (single `Result<string>` or an SSE stream mirroring ask). No
sources → a `chat`-style empty result the UI renders as a disabled/empty state. The Studio "Podsumowanie" tile calls
it; other tiles are `wkrótce` placeholders.

**Rationale**: Reuses the entire generation + retrieval + key stack; the only new surface is the prompt + endpoint.
