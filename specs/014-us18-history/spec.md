# Feature Specification: Historia rozmowy — wieloturowość + persystencja (US-18)

**Feature Branch**: `014-us18-history`

**Created**: 2026-07-13

**Status**: Draft

**Input**: User description: US-18 — a persisted, multi-turn conversation. The user holds a follow-up conversation ("a co z drugim punktem?") where the assistant remembers the context, and past conversations survive a reload — all within the user's own session.

## Context

Until now the chat thread lived **only in memory** (US-15) and reset on reload. US-18 makes conversations **first-class, persisted, and multi-turn**:

- **Persistence** — a `Conversation` (per session, with a scope and a title) holds ordered `Message`s (user + assistant), each with its **state** (answered / no-answer / interrupted) and its **sources** captured as JSON. Because sources are stored with their snippet/text, a historical citation stays verifiable even after its document is deleted (consistent with US-16).
- **Multi-turn context** — retrieval still runs **fresh for every question** (no query rewriting in the MVP), and the last **N** message pairs are added to the prompt as conversational context **alongside** the freshly retrieved passages.
- **Conversation management** — a per-session list of conversations, switching between them, and a "Nowa rozmowa" that starts an empty conversation. Titles are the first question truncated to 60 characters (no LLM titling).

This builds directly on US-13/14/15/16/17 (scope, ask+prompt, streaming, citations, message states) and the session-isolation guarantee of US-01.

## Clarifications

### Session 2026-07-13

- Q: When/how is the assistant message persisted after its answer streams? → A: **Via a durable integration event** — the user message is persisted synchronously when the ask starts; on stream completion the endpoint publishes a `ChatTurnCompleted`-style integration event (through the durable outbox) carrying the final state + sources, and a handler persists the assistant message. Decouples the write from the stream and survives a crash; the just-finished turn is persisted shortly after the stream ends (eventual consistency).
- Q: How is a Conversation created relative to asking? → A: **Explicit up-front** — "Nowa rozmowa" (and the initial app load when none exists) creates the conversation first; every ask carries its `conversationId`. The SSE stream contract is untouched (no new id returned through the stream).
- Q: Is a conversation's scope fixed or changeable mid-conversation? → A: **Changeable per ask** — each ask carries its own scope (US-13/14 request unchanged); the conversation records its **current/last-used** scope so reopening restores the selector. A new conversation defaults to "Wszystkie". (No per-message scope stored.)

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Follow-up questions with remembered context (Priority: P1) 🎯 MVP

Within a conversation, the user asks a follow-up that only makes sense given the prior turns ("rozwiń punkt drugi"), and the answer addresses the right thing because the recent conversation is part of the prompt.

**Why this priority**: Multi-turn memory is the whole point of the story — without it, each question is an island and "a co z drugim punktem?" is meaningless.

**Independent Test**: Seed a conversation with a prior Q&A about numbered contract points; ask a follow-up; assert the built prompt contains the recent messages **and** the freshly retrieved passages, and the assistant message is persisted.

**Acceptance Scenarios**:

1. **Given** a conversation whose first turn answered about several contract points, **When** the user asks "rozwiń punkt drugi", **Then** the prompt includes the recent conversation messages plus the freshly retrieved passages, and the answer refers to the correct point.
2. **Given** any question inside a conversation, **When** the answer completes, **Then** the user question and the assistant message (with its final state and its sources) are persisted to that conversation.

---

### User Story 2 - Reload and reopen a past conversation (Priority: P1)

Conversations survive a reload: the user reopens the app, picks a past conversation from the list, and sees every message rendered with its saved state and clickable citations.

**Why this priority**: Persistence across reloads is the visible payoff of the data model; without reload survival the feature is indistinguishable from the in-memory thread it replaces.

**Independent Test**: Persist a conversation with an answered turn (with sources), a no-answer turn, and an interrupted turn; load it via the API and assert each message renders with its saved state and citations resolved from stored snippets.

**Acceptance Scenarios**:

1. **Given** conversations from previous visits in the same session, **When** the user opens the app and selects one from the list, **Then** its messages render with their preserved states (NoAnswerFound, Interrupted) and clickable citations sourced from the stored snippets.
2. **Given** a persisted assistant message with citations whose document was later deleted, **When** the conversation is reopened, **Then** the citations still open a preview from the stored snippet (no dependency on the chunk still existing).

---

### User Story 3 - Start a new conversation (Priority: P1)

The user starts a fresh conversation, clearing the current context, while the previous one remains available in the list.

**Why this priority**: Without an explicit "new conversation", context bleeds across unrelated topics and the list never grows — it's the basic lifecycle control.

**Independent Test**: With an active conversation, trigger "Nowa rozmowa"; assert a new empty conversation exists with the default scope and the previous one is still listed.

**Acceptance Scenarios**:

1. **Given** an active conversation, **When** the user clicks "Nowa rozmowa", **Then** an empty conversation is started with the default scope ("Wszystkie"), and the previous conversation remains available in the list.
2. **Given** a brand-new conversation with no prior messages, **When** the user asks the first question, **Then** it behaves like a single-turn ask (no history in the prompt) and the conversation's title becomes that question truncated to 60 characters.

---

### User Story 4 - Bounded history in the prompt (Priority: P2)

Long conversations don't blow up the prompt: only the last N message pairs feed the model; older turns remain visible in the UI only.

**Why this priority**: An unbounded history would inflate cost/latency and eventually overflow the context window; the bound is a correctness and cost guard.

**Independent Test**: Build the prompt for a conversation with more than N pairs and assert only the last N pairs are included.

**Acceptance Scenarios**:

1. **Given** a conversation longer than N pairs (N from configuration), **When** the prompt is built, **Then** at most the last N pairs are included as conversational context; older turns appear only in the UI.

---

### User Story 5 - Session isolation of conversations (Priority: P2)

One session's conversations are invisible to another: requesting another session's conversation id returns "not found".

**Why this priority**: Conversations may contain sensitive question/answer content; the session-isolation guarantee (US-01) must extend to the new tables.

**Independent Test**: Create a conversation in session A; request it as session B; assert a 404 (existence not disclosed).

**Acceptance Scenarios**:

1. **Given** conversations owned by session A, **When** session B requests one of their ids, **Then** the system responds 404 (consistent with US-01), never revealing existence.

### Edge Cases

- **Delete a conversation** → hard delete, cascading to its messages, behind an in-app confirmation (design-system dialog, never `window.confirm`).
- **Conversation whose scope points at a since-deleted folder** → the conversation still loads normally; a **new** question in it fails with `ScopeNotFound` (US-13), surfaced as the existing scope error.
- **First question in an empty conversation** → behaves like US-14 (no history in the prompt).
- **Interrupted answer** → its partial text and Interrupted state are persisted and re-rendered on load.
- **Very long first question** → the title is truncated to 60 characters.
- **Concurrent asks** → one active generation at a time per the existing US-15 rule; the persisted message reflects the final state.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST persist a **Conversation** per session (id, session owner, scope, title, creation time) and its ordered **Messages** (id, conversation, role user/assistant, content, message state, sources, creation time).
- **FR-002**: On each ask within a conversation, the system MUST persist the user's question **synchronously when the ask starts**, and persist the assistant message **via a durable integration event published on stream completion** — carrying its **final state** (answered / no-answer / interrupted) and its **sources captured as data** (snippet/text + provenance), so a citation does not depend on the chunk still existing and the write survives a crash. The just-finished assistant turn is persisted shortly after the stream ends.
- **FR-003**: For a follow-up question, the system MUST run retrieval **fresh** for the current question (no query rewriting) and include the last **N** message pairs (N from configuration) as conversational context in the prompt **alongside** the freshly retrieved passages.
- **FR-004**: The prompt MUST include **at most** the last N pairs; older turns remain in the UI only. N is configuration-driven (no magic number).
- **FR-005**: Users MUST be able to **list** their conversations (per session, most-recent first), **open** one (loading its messages), **explicitly create** a new empty one up-front (default scope "Wszystkie") — "Nowa rozmowa", and the initial app load creates one when none exists — and **delete** one (hard delete cascading to its messages, behind an in-app confirmation). Every ask carries its `conversationId`.
- **FR-006**: A new conversation's **title** MUST be its first question truncated to 60 characters — no model call for titling.
- **FR-007**: Loading a conversation MUST render each message with its **preserved state** (answered / NoAnswerFound / Interrupted) and **clickable citations** resolved from the stored snippets/text.
- **FR-008**: Every Conversation and Message MUST carry the session owner and be subject to the same session-isolation guarantee as all other data: another session requesting a conversation's id gets **404**, never 403 or a disclosure of existence.
- **FR-009**: A conversation whose scope references a since-deleted target MUST still **load**; a **new** question in it MUST fail with the existing scope-not-found error (US-13), not a crash.
- **FR-010**: The multi-turn retrieval trade-off (a purely referential follow-up such as "rozwiń" may retrieve weakly because retrieval uses only the current question) MUST be documented, with condensing/query-rewriting named as future work.
- **FR-011**: The existing streaming contract (event names/order `sources` → `token`s → `done` / `error`, and the `done` state field from US-17) MUST be preserved; persistence is additive and MUST NOT change the stream shape.

### Key Entities

- **Conversation**: a session-owned thread — id, session owner, **current scope** (all / folder / document, as US-13; **changeable per ask** — updated to each ask's scope so reopening restores the selector; defaults to "Wszystkie" on creation), **title** (first question ≤60 chars), creation time. Created explicitly up-front. Has many Messages.
- **Message**: one turn in a conversation — id, parent conversation, **role** (user / assistant), **content** (question or answer text), **state** (for assistant messages: answered / no-answer / interrupted), **sources** (the numbered citations with their snippet/text + provenance, as data), creation time. Ordered by creation.
- **History window**: the last N Message pairs of a conversation supplied to the prompt as conversational context (N configuration-driven).
- **Message state**: reuses the states established in US-15/17 (Normal/answered, NoAnswerFound, Interrupted; Error is transient and not a persisted assistant message).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A follow-up question in a conversation yields a prompt containing both the recent conversation turns and freshly retrieved passages in 100% of asks that have prior turns.
- **SC-002**: 100% of completed asks persist the user question and the assistant message with its final state and sources; reopening the conversation reproduces every message with its state and clickable citations.
- **SC-003**: The prompt never includes more than N conversation pairs, for any conversation length.
- **SC-004**: A conversation's citations remain openable (from stored snippets) after their source document is deleted, in 100% of such cases.
- **SC-005**: Cross-session access to a conversation id returns 404 in 100% of attempts (no existence disclosure).
- **SC-006**: Starting a new conversation clears the visible context and preserves the previous conversation in the list, every time.

## Assumptions

- The ask/stream contract (US-14/15), the message states (US-15/17), the citation sources with snippet/text (US-16), the scope model + `ScopeNotFound` (US-13), and the session-isolation mechanism (US-01, EF global query filter via the session context) exist on master and are reused.
- Persistence uses the existing datastore and the migrations project; no schema is applied at application startup.
- Auditable fields and UTC timestamps follow the existing central convention (interceptor + injected time source).
- Tests never call the real model provider; a scriptable fake generator drives answered / no-answer / interrupted turns.
- Design decisions resolved in the 2026-07-13 clarify session: the assistant message is persisted **via a durable integration event** on stream completion (user message persisted synchronously at ask start); a conversation is **created explicitly up-front** and every ask carries its id; scope is **changeable per ask**, with the conversation holding the current/last-used scope. See the Clarifications section.
- Out of scope: query rewriting/condensing, searching within history, exporting a conversation, LLM-generated titles, and sharing conversations across sessions.
