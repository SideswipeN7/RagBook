# Phase 1 Data Model — US-21

## Stage 2 schema change (one migration)

| Table | Change |
|---|---|
| `documents` | `+ conversation_id uuid NULL` (index; FK → `conversations(id)` `ON DELETE CASCADE`). |
| `folders` | `+ conversation_id uuid NULL` (index; FK → `conversations(id)` `ON DELETE CASCADE`). |

Legacy rows keep `conversation_id = NULL` (hidden from the per-conversation view). Demo documents keep it `NULL`
(they are global, read by `Origin == Demo`). Deleting a `conversations` row cascades its folders + documents; the
existing `chunks` FK cascades from documents.

## Entity changes

| Type | Change |
|---|---|
| `Document` (Documents) | `+ Guid? ConversationId` (set by `CreateUpload`; `CreateForQuota`/`CreateDemo` leave null). |
| `Folder` (Folders) | `+ Guid? ConversationId` (set on create when a conversation is active). |
| `UploadDocumentCommand` | `+ Guid? ConversationId`. |
| `CreateFolderCommand` | `+ Guid? ConversationId`. |
| `GetTreeQuery` | `+ Guid? ConversationId` (scope the tree read). |
| `AskQuestionCommand`/pipeline | thread `ConversationId` → `IScopedRetriever.RetrieveAsync`. |

## Read-path predicates (Stage 2)

- `TreeReader`: documents/folders `WHERE conversation_id = @conv` (session filter still applies); demo docs global.
- `ScopedRetriever.FilterClause` (non-demo): `d.user_session_id=@session AND d.status=@ready AND d.conversation_id=@conv AND (ScopePredicate)`.

## Stage 3 — summary

| Item | Shape |
|---|---|
| `SummarizeConversationCommand` | `Guid ConversationId` → `Result<string>` (or an SSE stream). |
| endpoint | `POST /api/conversations/{id}/summary` → the generated summary; no ready sources → empty result. |

## Frontend state

- `workspace.store.ts`: `activeConversationId` (shared), `conversationsCollapsed` / `sourcesCollapsed` signals,
  `configured` computed (`apiKey active || demo available || user-continued-readonly`).
- `tree.store` / `document-upload.store`: pass `workspace.activeConversationId` on refresh/upload.
- `studio` component: `summary` signal + `loadSummary()` (POST); empty/disabled states.

No quota change (session-wide).
