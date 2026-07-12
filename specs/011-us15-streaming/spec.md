# Feature Specification: Streaming odpowiedzi — chat UI + SSE hardening (US-15)

**Feature Branch**: `011-us15-streaming`

**Created**: 2026-07-12

**Status**: Draft

**Input**: User description: "US-15 — Streaming odpowiedzi. Frontend czatu konsumujący strumień SSE z US-14 (fetch + ReadableStream): pole pytania, selektor scope, render token-po-tokenie, Stop, stany. Plus hardening backendu (heartbeat, anulowanie na rozłączeniu)."

## Clarifications

### Session 2026-07-12

- Q: How should US-15 show the conversation within a session (cross-reload persistence is US-18)? → A: **Multi-turn** — a growing in-memory **list of question/answer exchanges** (a real chat thread) with auto-scroll; new questions append at the bottom. Held in the client only; a reload clears it (history is US-18).

## Boundary note (US-15 vs US-14/16/17/18)

The streaming answer **backend** already exists from **US-14** (`POST /api/chat/ask` → `text/event-stream` with `sources` / `token` / `done` / `error` events, and a `CancellationToken` that flows to generation). US-15 **consumes and hardens** it — it does not change that contract. US-15 delivers the **chat UI** (question input, scope selector, live token-by-token rendering, Stop, states) plus small backend hardening (a periodic keep-alive comment; verified cancellation on client disconnect). **Clickable citations** (`[n]`→document) are **US-16**; the full **"no basis" refusal** UX is **US-17**; **conversation history/persistence** is **US-18**.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Live streamed answer (Priority: P1)

A user with an active key asks a question and watches the answer appear **token by token**, smoothly appended (no whole-block flicker), starting as soon as generation begins.

**Why this priority**: This is the visible payoff of the whole RAG pipeline — a responsive, live answer.

**Independent Test**: With a mocked stream, ask a question; the answer text grows incrementally in the UI (multiple appends), and the source list appears once the `sources` event arrives, before completion.

**Acceptance Scenarios**:

1. **Given** an active key and a question, **When** the answer generates, **Then** the first text appears in the UI promptly after generation starts and is appended smoothly as more arrives (not re-rendered as one block).
2. **Given** a correct generation, **When** the stream completes, **Then** the UI has consumed the events in their real order — `sources` first, then the incremental answer text, then completion — rendering the source list after `sources`.

---

### User Story 2 - Stop an in-progress answer (Priority: P1)

While an answer is streaming, the user clicks **Stop**; the stream closes, the partial answer stays in the conversation marked **interrupted**, and the backend generation is cancelled (no wasted provider tokens).

**Why this priority**: Control and cost — a user must be able to halt a long or wrong answer, and the app must not keep paying for it.

**Independent Test**: Start a stream (slow mock), click Stop; the UI stops appending, marks the message interrupted, keeps the partial text, and the backend observes the cancellation.

**Acceptance Scenarios**:

1. **Given** a streaming answer, **When** the user clicks Stop, **Then** the client aborts the request, the partial answer remains visible marked "interrupted", and no further text is appended.
2. **Given** the client aborts, **When** the server notices the disconnect, **Then** the generation is cancelled (verifiable: the generator observes cancellation) — no hanging provider call.

---

### User Story 3 - Mid-stream error is clear (Priority: P1)

If the stream fails partway — an `error` event or the stream ending without completion — the UI shows the partial text **plus a clear error message** with a **Try again** action, never an unexplained truncated answer.

**Why this priority**: A silent cut-off erodes trust; the user needs to know it failed and how to retry.

**Independent Test**: Feed a stream that emits an `error` event (and separately, one that ends without `done`); the UI shows the partial text + a readable, code-mapped message + Try again.

**Acceptance Scenarios**:

1. **Given** a streaming answer, **When** an `error` event arrives with a code, **Then** the UI keeps the partial text and shows the mapped human message (PL) with a **Try again** action.
2. **Given** a stream that ends **without** a completion signal, **When** the client detects the truncation, **Then** it is treated as an error (partial text + message + retry), not a clean answer.

---

### User Story 4 - Choose the scope (Priority: P1)

The user picks what the question searches — **all documents**, a **folder** (with its subtree), or a **single document** — from the existing tree, shown as a chip above the input; the choice is sent with the question.

**Why this priority**: Scope is central to RAG relevance (US-13/14); without a selector the user can only ask "all".

**Independent Test**: Open the scope selector, pick a folder (and separately a ready document); the active-scope chip updates and the next question is sent with that scope.

**Acceptance Scenarios**:

1. **Given** the tree has folders and ready documents, **When** the user opens the scope selector, **Then** they can choose "All documents", a folder, or a **ready** document (documents still processing are not selectable).
2. **Given** a chosen scope, **When** the user asks, **Then** the question is sent with that scope and the active-scope chip reflects it.

---

### User Story 5 - Locked and no-basis states (Priority: P2)

Without an active key the question field is locked with a link to settings; when the answer has no grounds, the user sees a neutral "no basis in the selected scope" note instead of a fabricated answer.

**Why this priority**: Correct empty/locked states keep the chat honest and guide the user; the full refusal UX is US-17.

**Independent Test**: With no key, the input is disabled + points to settings; with a `done{groundsFound:false}` stream, the UI shows the neutral no-basis note (no answer text).

**Acceptance Scenarios**:

1. **Given** no active key (non-demo), **When** the user views the chat, **Then** the question field is disabled with a message and a link to settings (reusing the key state).
2. **Given** a completed stream reporting no grounds, **When** it ends, **Then** the UI shows a neutral "no basis in the selected scope" message rather than an answer (full refusal UX in US-17).

---

### Edge Cases

- **Very short answer** (fast finish) → the event sequence is still fully consumed (`sources` → `token`(s) → `done`).
- **Two questions in quick succession** → the previous stream is aborted; only the newest is active (one active generation).
- **Stream ends without completion** → treated as an error (US3).
- **File scope pointing at a not-ready document** → only ready documents are selectable, so this cannot be chosen.
- **Long-running generation behind a proxy** → a periodic keep-alive prevents an idle-timeout from cutting the stream.
- **Tab closed mid-stream** → the server cancels generation on disconnect.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The chat UI MUST send a question + selected scope to the existing ask endpoint and consume its event stream, appending answer text **incrementally** as `token` events arrive (no whole-block re-render).
- **FR-002**: The UI MUST consume the events in their real order — `sources` (the numbered passages) first, then `token` deltas, then the completion signal — and render the source list once `sources` is received (clickable citations are US-16).
- **FR-003**: The UI MUST provide a **Stop** control during streaming that aborts the request; the partial answer MUST remain, marked **interrupted**, and no further text is appended.
- **FR-004**: On the client aborting, the **backend MUST cancel** the generation (the request cancellation flows to the generator) so no provider call hangs.
- **FR-005**: On an `error` event (with a code) or a stream that ends **without** completion, the UI MUST keep the partial text and show a **readable, code-mapped (PL)** error with a **Try again** action.
- **FR-006**: The UI MUST offer a **scope selector** — All documents / a folder (subtree) / a single **ready** document — sourced from the existing tree; the active scope MUST be shown as a chip and sent with the question. Documents that are not ready MUST NOT be selectable.
- **FR-007**: When there is no active key (non-demo), the question field MUST be **disabled** with a message and a link to settings (reusing the existing key state); when a completed stream reports **no grounds**, the UI MUST show a neutral **no-basis** message (full refusal UX is US-17).
- **FR-008**: Only **one** generation MUST be active per chat at a time — asking again MUST abort the previous stream and start the newest.
- **FR-009**: The chat MUST show a **multi-turn thread** — a growing in-memory list of question/answer exchanges (new asks append at the bottom; a reload clears it, persistence is US-18) — and **auto-scroll** to follow new text, stopping if the user scrolls up ("detach") and resuming when they return to the bottom.
- **FR-010**: The backend MUST emit a periodic **keep-alive** (an SSE comment) at a configurable interval during a long generation so an intermediary idle-timeout does not cut the stream, and MUST send correct streaming headers (no-cache, unbuffered).
- **FR-011**: No automated test may call the real generation provider — the frontend tests drive a mocked stream and the backend tests a fake generator (reused from US-14).

### Key Entities *(include if feature involves data)*

- **Chat thread (view state)**: an ordered, in-memory list of exchanges for the session. Each **exchange** holds the user question and the assistant answer, with a status — `streaming` / `complete` / `interrupted` / `error` — its accumulating text, the received sources, and (on failure) the error code/message. Held in the client only; cleared on reload (persistence is US-18).
- **Chat scope selection**: the current `all` / `folder(id)` / `document(id)` choice, derived from the tree, shown as a chip and sent with each question.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: During a streamed answer the UI updates **multiple times** as text arrives (incremental), with the first update appearing before completion — in **100%** of streamed answers.
- **SC-002**: Clicking Stop halts appending, marks the message interrupted while keeping the partial text, and the backend generation is cancelled — **100%** of the time, with **0** further provider tokens consumed after Stop.
- **SC-003**: A mid-stream `error` event or an incomplete stream always results in a visible error message + retry (never a silent truncation) — **100%** of failure cases.
- **SC-004**: The user can scope a question to all / a folder / a ready document, and the question is sent with the chosen scope — **100%** of asks reflect the selected scope.
- **SC-005**: Asking a second question while one streams leaves exactly **one** active stream (the newest); the prior is aborted.
- **SC-006**: A long generation is not cut by an idle intermediary — a keep-alive is emitted at least once per configured interval.

## Assumptions

- The streaming contract is **US-14's as-built** (`POST /api/chat/ask`, events `sources` / `token` / `done{groundsFound}` / `error{code}`); US-15 consumes these exact names and does not rename them (the US-15 source doc's "delta" == US-14's `token`, and US-14 emits `sources` **first**, not last).
- Because the request is a `POST` with a body, the client consumes the stream via a streaming fetch + an SSE parser (native `EventSource` cannot POST). Aborting uses the platform abort mechanism, which closes the connection so the server-side request cancels.
- The key state (`chatLocked`) and the tree data already exist (US-02, US-07) and are reused; the chat mounts in the app shell (no router yet).
- Error codes are mapped to Polish user messages with a code→message table, as existing stores do; unknown codes get a generic message.
- The keep-alive interval and streaming headers are configured server-side; the exact interval is a tuning value.

## Out of Scope

- Clickable citations and `[n]`→document navigation in the UI (**US-16**).
- Full "no basis" refusal detection/UX and the sentinel (**US-17**).
- Conversation persistence / history across reloads (**US-18**).
- Stream resumption after a drop, and WebSocket transport.
- Any change to US-14's SSE event contract or endpoint.
