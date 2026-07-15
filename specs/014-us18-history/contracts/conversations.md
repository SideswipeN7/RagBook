# Contract — Conversations + history (US-18)

New REST endpoints for conversation management + an additive `conversationId` on the ask. The SSE stream shape
(`sources` → `token*` → `done{groundsFound,state}` / `error`) is **unchanged**.

## REST — `/api/conversations`

All are session-scoped (the global filter). Another session's id → **404** (never 403 / disclosure).

### `GET /api/conversations`
List the session's conversations, most-recent first.
```
200 → ConversationSummaryDto[]   // { id, title, scopeType, scopeTargetId, createdAt }
```

### `POST /api/conversations`
Create an empty conversation (explicit, up-front).
```
body: { scope: { type: "all"|"folder"|"document", targetId?: uuid } }   // defaults to all
201 → ConversationSummaryDto
```

### `GET /api/conversations/{id}`
Load a conversation with its ordered messages.
```
200 → ConversationDetailDto { ...summary, messages: MessageDto[] }
      MessageDto { id, role: "user"|"assistant", content, state?: "answered"|"no_answer"|"interrupted",
                   sources?: SourceDto[], createdAt }
404 → ProblemDetails { code: "chat.conversation_not_found" }   // also for another session's id
```

### `DELETE /api/conversations/{id}`
Hard-delete the conversation and (cascade) its messages.
```
204
404 → ProblemDetails { code: "chat.conversation_not_found" }
```

## Ask — `POST /api/chat/ask` (extended, additive)

```
body: { conversationId: uuid, question: string, scope: ScopeDto }   // conversationId is new
```
- **Guard** (pre-generation): `conversationId` must resolve to a conversation in the current session, else
  `404 chat.conversation_not_found` as ProblemDetails (before any provider call, alongside the existing
  key/scope guards).
- **On start**: persist a `user` `Message`; if the conversation title is empty, set it to `question` truncated to
  `ChatOptions.TitleMaxChars`; update the conversation's current scope.
- **Prompt**: `PromptBuilder.Build(question, retrievedChunks, history)` where `history =
  ConversationHistory.SelectRecent(messages, ChatOptions.HistoryPairs)`.
- **Stream**: unchanged (`sources`/`token`/`done`/`error`).
- **On completion / disconnect**: publish `ChatTurnCompleted` (below). No change to the streamed events.

## Integration event — `ChatTurnCompleted : IExternalEvent`

Published by the ask endpoint after the stream ends (including client-disconnect → interrupted); routed to the
durable outbox; a handler persists the assistant `Message`.
```csharp
record ChatTurnCompleted(
    Guid ConversationId,
    Guid UserSessionId,
    string Answer,                 // accumulated text (partial if interrupted)
    string State,                  // "answered" | "no_answer" | "interrupted"
    string? SourcesJson) : IExternalEvent;
```
Handler → `Message.Assistant(conversationId, answer, state, sourcesJson)` persisted under the event's session.

## Invariants

1. SSE event names/order and the `done` payload are unchanged; `conversationId` is a request-body addition only.
2. Every conversations/messages read is session-filtered; cross-session id → 404.
3. Deleting a conversation cascades to its messages (DB FK), behind an in-app confirm.
4. A user message is durable from ask-start; the assistant message lands shortly after stream end (eventual).
5. Citations load from `sources_json` — no dependency on the chunk still existing (US-16 AC-4).
