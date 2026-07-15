# Phase 0 Research — US-18 Historia rozmowy

## D1 — Module placement (new module vs Chat slice)

**Decision**: Put `Conversation`/`Message` and the `Conversations/*` slices **inside the Chat module**.

**Rationale**: The ask flow writes messages, history feeds the prompt, and scope reuses US-13 — conversations are
intrinsic to Chat, not a standalone bounded context. Keeps the vertical slice cohesive; folders are referenced
only by scope id (no cross-module reference), exactly as US-13 already does.

**Alternatives rejected**: A new `Conversations` module (splits tightly-coupled behaviour across modules, forces
event/round-trips between Chat and Conversations for the write path).

## D2 — Assistant-message persistence: durable event (clarify Q1)

**Decision**: The **user** message is persisted **synchronously at ask start** (before streaming). The
**assistant** message is persisted via a **`ChatTurnCompleted : IExternalEvent`** published on stream completion
(and on client-disconnect with the Interrupted state + partial text) → Wolverine **durable outbox** → a handler
in `Features/AskQuestion` persists the assistant `Message` (content, state, sources JSON).

**Rationale**: Chosen in clarify for durability/decoupling — the write survives a crash and is off the stream's
hot path. The user message is written up front so the turn is never lost even if generation fails before the
event. Matches the constitution's `IExternalEvent` → outbox path (§II).

**Infra note (verified)**: The durable outbox is **already configured** — `Program.cs` calls
`options.PersistMessagesWithPostgresql(connectionString)` (guarded by `Wolverine:DurabilityEnabled`, default
true), stood up for US-06. `ChatTurnCompleted` routes through it; no new messaging infrastructure or envelope-table
migration is required.

**Session-context note (important)**: the outbox handler runs **outside the HTTP request's session**, so the
`SessionStampingInterceptor` + global query filter won't apply the user's session. The handler MUST set the
assistant `Message.UserSessionId` (and audit actor) **from the event's `UserSessionId`**, not from the ambient
`ISessionContext`.

**Trade-off**: The just-finished assistant turn is **eventually consistent** — a reload immediately after the
stream ends may briefly precede the outbox handler. Acceptable for the MVP; the frontend already holds the
streamed answer in memory, so the user sees it instantly regardless of when the row lands.

**Alternatives rejected**: Synchronous write in the endpoint after the stream (simpler, but the clarify decision
prefers durability; a mid-write crash would lose the turn). Persisting both messages in one end-of-stream event
(would lose the user question on a crash before completion).

## D3 — Conversation creation: explicit up-front (clarify Q2)

**Decision**: A conversation is **created explicitly** — "Nowa rozmowa" and the initial app load (when none
exists) `POST /api/conversations`; every ask carries `conversationId` in its body.

**Rationale**: Keeps the SSE stream contract untouched (FR-011) — no need to invent a way to return a freshly
minted id through the token stream. Deterministic: the frontend always has an active conversation before asking.

**Alternatives rejected**: Lazy creation on first ask (threads a new id back through the streaming response — a
contract change the spec avoids).

## D4 — Scope: changeable per ask (clarify Q3)

**Decision**: Each ask carries its own scope (US-13/14 request **unchanged**); the conversation stores its
**current/last-used** scope, updated on each ask, so reopening restores the scope selector. New conversation
defaults to `All` ("Wszystkie"). No per-message scope is stored.

**Rationale**: Preserves the live per-turn scope selector from US-15 (no UX regression) while giving the
conversation a single scope to restore. The scope-on-deleted-folder edge case is unchanged: the ask still carries
the scope, so a new question resolves `ScopeNotFound` (US-13) exactly as today.

**Alternatives rejected**: Fixed-per-conversation scope (would move the selector to creation-time and lose
mid-conversation flexibility); per-message scope (needless — messages don't render scope in the UI).

## D5 — Scope persistence shape

**Decision**: Store scope on `conversations` as two columns — `scope_type` (`all|folder|document`) +
`scope_target_id` (`uuid null`) — mapped from the existing `ChatScope` value object (US-13). Reconstruct
`ChatScope` on read via its factories.

**Rationale**: `ChatScope` is factory-constructed (invalid combos unrepresentable); two columns are the minimal
faithful projection and query-friendly. Avoids an owned-type/JSON blob for a two-field value.

## D6 — Message.sources persistence

**Decision**: Persist assistant sources as **`sources_json jsonb`** — the same `SourceDto` shape the `sources` SSE
event already carries (`number, documentId, fileName, pageNumber, text, chunkId`). On load, the API returns them
and the frontend maps them straight to its `Source[]`, so citations + preview (US-16) work unchanged and survive
document deletion (US-16 AC-4).

**Rationale**: Reuses the exact wire shape end-to-end; no chunk lookup on load; deletion-independent by
construction. User messages and no-answer/deterministic turns store `null`/empty sources.

## D7 — Multi-turn prompt shape + bound

**Decision**: A pure `ConversationHistory.SelectRecent(messages, ChatOptions.HistoryPairs)` returns the last N
(user, assistant) pairs; `PromptBuilder.Build(question, chunks, history)` prepends them as a short conversational
transcript **before** the numbered passages, keeping the grounding instructions and `[n]` citation rules intact.
Retrieval is still driven by the **current question only** (no rewriting).

**Rationale**: Bounds cost/context (FR-004) with a unit-testable selection rule; keeps grounding/citation
semantics (US-14/16) unchanged. The purely-referential-follow-up weakness ("rozwiń" retrieves on a thin query) is
the documented trade-off (FR-010; README + future work: condensing question).

**Alternatives rejected**: Feeding full history (unbounded cost/overflow); condensing/rewriting the query
(explicitly out of scope for the MVP).

## D8 — Frontend: conversation-backed ChatStore

**Decision**: Add `conversations.store.ts` (signals + Angular `HttpClient`) for list/get/create/delete + `activeId`.
Rework `chat.store.ts` to be conversation-backed: on open, map loaded `Message[]` → `ChatExchange[]` (with saved
`status` + `sources`); `ask` carries `activeId`. `chat.store` keeps **`fetch`** for the SSE stream (HttpClient
can't stream POST-SSE — US-15). A `conversation-list` component (sidebar) offers "Nowa rozmowa" + delete behind
the shared design-system confirm dialog (never `window.confirm`).

**Rationale**: Splits plain-JSON CRUD (HttpClient, testable with `HttpTestingController`) from the streaming path
(fetch). Reuses `chat-answer` (US-16/17) for rendering loaded messages — states + citations come for free.

**Alternatives rejected**: One mega-store (mixes CRUD + streaming concerns); server-render of history (the SPA is
signal-driven).

## D9 — ChatOptions

**Decision**: New `ChatOptions` (`SectionName = "Chat"`) with `HistoryPairs = 6` and `TitleMaxChars = 60`;
registered in `Program.cs` (`Configure<ChatOptions>`). Retrieval/threshold stay in `RagOptions`.

**Rationale**: Constitution §VII (config-driven limits, zero magic numbers); the doc names `ChatOptions.HistoryPairs`.
Keeping RAG params in `RagOptions` and conversation params in `ChatOptions` is a clean split.
