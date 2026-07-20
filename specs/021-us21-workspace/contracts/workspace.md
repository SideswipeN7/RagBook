# Contract — Workspace redesign (US-21)

## Stage 1 — shell (no API change)

Pure frontend: a config-first gate then a 4-column grid (conversations | sources | chat | Studio), one shared active
conversation. No endpoint changes.

## Stage 2 — per-conversation sources (API changes)

- `POST /api/documents` (multipart) — **+ `conversationId`** form field: the uploaded document is pinned to it.
- `GET /api/tree?conversationId={id}` — returns **that conversation's** folders + documents (+ the global `demo[]`).
  A missing/foreign conversation → the empty own-set (demo still shown).
- `POST /api/folders` — **+ `conversationId`** in the body: the new folder belongs to that conversation.
- `POST /api/chat/ask` — unchanged shape (`{ conversationId, question, scope }`); retrieval is now additionally
  scoped to the conversation's sources (`all`/`folder`/`document` all fenced by `conversation_id`; `demo` unchanged).
- `DELETE /api/conversations/{id}` — now **cascades** the conversation's folders, documents, and chunks.

Isolation: every read stays session-scoped; the conversation id is an extra fence, a foreign id is invisible.

## Stage 3 — Studio summary

- `POST /api/conversations/{id}/summary` → a generated **summary** of the conversation's ready sources (on the BYOK
  or demo key). `200` with the summary; a conversation with **no ready sources** → an empty/disabled result the UI
  renders as a neutral state (not an error); a foreign conversation → `404`; no key/demo → the existing key-missing /
  demo-unavailable errors.

## Invariants

- A document/folder belongs to exactly one conversation (or none = legacy/hidden); demo docs are global read-only.
- A question is grounded only on the active conversation's ready sources.
- Deleting a conversation removes its whole notebook (folders + sources + chunks); quota drops accordingly.
- Quota remains session-wide; session isolation is preserved throughout.

## Frontend consumption

- The shell reads one `workspace.activeConversationId`; selecting a conversation updates every column.
- Sources column = the active conversation's tree (drag-drop + multi-select within it); demo section read-only.
- Studio "Podsumowanie" posts to the summary endpoint and renders the result / empty state; other tiles are `wkrótce`.
