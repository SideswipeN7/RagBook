# Feature Specification: Tryb demo — demo mode (US-03)

**Feature Branch**: `018-us03-demo-mode`

**Created**: 2026-07-16

**Status**: Draft

**Input**: User description: US-03 — a visitor tries the assistant on built-in, read-only sample documents **without
supplying their own API key**, getting a full streaming RAG answer with citations in under a minute, behind
per-session and per-IP usage limits paid for by a server-held application key.

## Context

Demo mode lets an unauthenticated visitor (e.g. a recruiter) experience the product end-to-end in a minute: pick
the **"Dokumenty demo"** scope, ask a question, and get a grounded, streaming answer with citations — all on a
server-configured **application key**, never their own. A small set of **globally-visible, read-only** demo
documents is seeded once at startup and appears in every session. Cost is bounded by two configured limits: a
**per-session question count** and a **per-IP hourly rate**. This builds on US-01 (session isolation), US-02
(BYOK key handling), US-05 (quota — demo already excluded), and US-14–US-17 (chat/RAG/citations/grounding), which
are all merged. Only the demo experience is added; the user's own knowledge base and BYOK flow are unchanged, and
an upload made while browsing demo always lands in the user's own resources.

## Clarifications

### Session 2026-07-16

- Q: How should globally-visible demo documents coexist with the per-session query filter? → A: **Sentinel
  demo-session id** — demo documents are seeded under a fixed constant `DemoSessionId` by initializing the session
  context to it for the seeder's scope (the existing stamping interceptor writes it; no interceptor change). Demo
  reads (retrieval and the demo tree section) bypass the session filter and select by `Origin == Demo`; `Origin` is
  the read discriminator, the sentinel id keeps seed writes consistent. No "ownerless" row shape, and the shared
  isolation interceptor is untouched.
- Q: What HTTP shape should the per-session demo-question limit return, and what counter lifetime? → A: **429
  RateLimited, per-session lifetime** — `DemoLimitReached` is an `Error.RateLimited` with a distinct stable code
  (e.g. `chat.demo_limit_reached`) → `429` via the existing `ErrorStatusMapper`, consistent with
  `chat.provider_rate_limited` / `settings.too_many_attempts`. The per-session counter is a **lifetime** count
  (resets only with a new session), distinct from the per-IP **hourly window** (FR-007). The frontend branches on
  the code to show "X / N pytań demo" + the BYOK nudge.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Ask the demo without an API key (Priority: P1) 🎯 MVP

A visitor with no API key selects the demo scope and asks a question, receiving a full streaming RAG answer with
citations drawn from the demo documents, paid for by the application key.

**Why this priority**: This is the entire value of the story — a keyless, one-minute "it works" experience is the
case-study centrepiece.

**Independent Test**: In a fresh session that has set no API key, choose "Dokumenty demo", ask a question about the
seeded content → tokens stream in, and at least one citation points to a demo document.

**Acceptance Scenarios**:

1. **Given** a new session with no API key, **When** the visitor selects "Dokumenty demo" and asks a question,
   **Then** they get a full RAG answer (streaming + citations) generated on the application key.
2. **Given** the demo documents are seeded, **When** the visitor opens the app, **Then** a read-only "Demo" section
   lists them and they are selectable as the chat scope.

---

### User Story 2 - Per-session question limit (Priority: P1)

After a configured number of demo questions in a session, further demo questions are refused with a clear counter
and a nudge toward supplying an own key (BYOK).

**Why this priority**: Without the per-session cap the application key's cost is unbounded — the limit is what makes
keyless demo affordable to offer.

**Independent Test**: In one session, ask the configured number of demo questions, then ask one more → the extra is
refused with a demo-limit error; the UI shows "X / N pytań demo" and a BYOK nudge.

**Acceptance Scenarios**:

1. **Given** a session that has asked N demo questions, **When** it asks another demo question, **Then** the request
   is refused with a demo-limit failure and no answer is generated.
2. **Given** any demo session, **When** it asks a demo question within the limit, **Then** the remaining-questions
   counter shown to the user decreases by one.

---

### User Story 3 - Per-IP hourly rate limit (Priority: P2)

An IP that exceeds the configured hourly demo rate is throttled with a standard rate-limit response that tells the
client when to retry.

**Why this priority**: The per-session cap resets with a new session; the per-IP hourly limit is the backstop
against one visitor (or bot) draining the key across many sessions.

**Independent Test**: From one IP, exceed the configured hourly demo rate → the next demo request is rejected with a
rate-limit response carrying a retry hint; the UI shows a readable "try again later" message.

**Acceptance Scenarios**:

1. **Given** an IP over its hourly demo limit, **When** it sends another demo request, **Then** it receives a
   rate-limit rejection that indicates when to retry.

---

### User Story 4 - Demo documents are read-only (Priority: P1)

The seeded demo documents cannot be deleted, moved, or overwritten by any session; the UI presents them without
mutating controls.

**Why this priority**: Demo documents are shared global resources — one visitor must never be able to change or
remove what every other visitor sees.

**Independent Test**: Attempt to delete or move a demo document (single or bulk) → the operation is refused as
read-only; the demo section shows no move/delete controls.

**Acceptance Scenarios**:

1. **Given** a demo document, **When** any session attempts to delete, move, or bulk-operate on it, **Then** the
   operation is refused as read-only and the document is unchanged.
2. **Given** the demo section in the tree, **When** it is rendered, **Then** it carries a read-only badge and offers
   no move/delete actions.

---

### User Story 5 - Demo does not consume the user's quota (Priority: P2)

Demo documents never count toward the user's file/storage quota; a visitor can still upload their own files up to
the full limit regardless of how many demo documents exist.

**Why this priority**: Demo resources are the product's, not the user's — counting them would wrongly shrink the
user's allowance. (Already enforced from US-05; this story adds a regression guard.)

**Independent Test**: With demo documents present, read the quota → the used-document count and storage exclude the
demo documents; a fresh session can upload up to the full user limit.

**Acceptance Scenarios**:

1. **Given** seeded demo documents, **When** the quota is computed for a session, **Then** the demo documents are
   not counted toward the user's document or storage limits.

---

### Edge Cases

- **Upload while browsing demo**: an upload is always stored in the **user's** own resources, never as a demo
  document, regardless of the currently-selected chat scope.
- **Application-key budget exhausted / provider error**: a demo answer that fails at the provider surfaces a
  readable "Tryb demo chwilowo niedostępny" message (a mapped provider error), never a raw 500.
- **Seeding idempotency**: seeding runs at startup against fixed ids — it must be a no-op on an already-seeded
  database and correct on a clean database and across restarts (no duplicates).
- **Demo scope with no key set**: the demo path must not be blocked by the BYOK "api key missing" guard — that guard
  applies only to the user's own scopes.
- **A user's own scope with no key**: unchanged — still refused with the existing "api key missing" behaviour.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST seed a small set (2–3) of **read-only demo documents** (with their indexed content) at
  startup, **idempotently** by fixed ids, so they exist exactly once on a clean database and are unchanged across
  restarts.
- **FR-002**: Demo documents MUST be **globally visible** — present in every session — while remaining outside any
  single session's ownership, consistent with session isolation (a demo document is shared, not another session's
  private resource). They are seeded under a fixed sentinel demo-session id and read by `Origin == Demo` with the
  per-session filter bypassed; the shared stamping interceptor is not modified.
- **FR-003**: A visitor MUST be able to run a full RAG chat (streaming answer + citations) over the demo documents
  **without setting an API key**, generated on a **server-configured application key** that is never exposed to the
  client.
- **FR-004**: The demo chat path MUST NOT be blocked by the BYOK "api key missing" guard; that guard MUST continue
  to apply to the user's own (non-demo) scopes.
- **FR-005**: The system MUST limit demo questions to a **configured maximum per session** (a per-session lifetime
  count); beyond it, a demo question MUST be refused with a distinct demo-limit failure (`429`, stable code
  `chat.demo_limit_reached`) and **no** answer generated.
- **FR-006**: The system MUST expose, to the UI, how many demo questions remain in the session so it can show
  "X / N pytań demo" and a BYOK nudge when exhausted.
- **FR-007**: The system MUST enforce a **configured per-IP hourly rate limit** on demo requests, responding with a
  standard rate-limit rejection that indicates when the client may retry.
- **FR-008**: Demo documents MUST be **read-only**: any attempt to delete, move, or bulk-operate on them MUST be
  refused with the stable read-only outcome, and the UI MUST present them without mutating controls.
- **FR-009**: Demo documents MUST NOT count toward the user's file/storage quota, and an upload made while the demo
  scope is selected MUST be stored as the user's own resource.
- **FR-010**: A demo answer that fails at the provider (e.g. exhausted application-key budget) MUST surface a
  readable, mapped "demo temporarily unavailable" message, never a raw server error.
- **FR-011**: All demo limits and the application key MUST be **configuration-driven** (per-session max, per-IP
  hourly max, demo seed manifest/ids, application key) — no magic numbers, and the key never committed to the repo.

### Key Entities

- **Demo document**: a seeded, read-only, globally-visible document (+ its chunks/embeddings) that any session can
  read and chat over but none can mutate; excluded from user quota.
- **Application key**: a server-held provider credential used to generate demo answers; never exposed to clients;
  its cost bounded by the demo limits.
- **Demo scope**: a chat scope that retrieves only demo documents (independent of session ownership).
- **Demo usage counters**: the per-session question count and the per-IP hourly request count that gate demo usage.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: From a fresh, keyless session a visitor can obtain a grounded demo answer with at least one citation in
  a single interaction — no key entry, no setup — in 100% of attempts while within limits.
- **SC-002**: Demo documents appear in **every** session (100% cross-session visibility) and are seeded exactly once
  regardless of restart count (no duplicates ever).
- **SC-003**: Once a session reaches its demo-question limit, 100% of further demo questions are refused before any
  generation, and the remaining-count shown to the user is always accurate.
- **SC-004**: An IP over the hourly demo limit is throttled with a retry indication in 100% of over-limit attempts.
- **SC-005**: 0 demo documents can be deleted, moved, or bulk-operated by any session; 0 demo documents count toward
  any user's quota.
- **SC-006**: A provider failure during a demo answer yields a readable "temporarily unavailable" message in 100% of
  such cases (never a raw 500).

## Assumptions

- The demo document set is small and fixed (2–3 files); per-user configurable demo sets are out of scope.
- The application key is provisioned via environment / secret store in each environment; local/dev may leave it
  unset, in which case demo generation surfaces the same "temporarily unavailable" path rather than crashing.
- The per-session question count is a **per-session lifetime** count (not a sliding window); the per-IP limit is an
  **hourly window** (the two limits are deliberately different shapes — see Clarifications).
- Existing chat streaming, retrieval, citation, and grounding behaviour (US-13–US-17) is reused unchanged for demo
  answers; only the scope and the key source differ.

## Dependencies

- **US-01** session isolation, **US-02** BYOK key handling, **US-05** quota (demo already excluded),
  **US-14–US-17** chat/RAG/citations/grounding — all merged on master.

## Out of Scope

- A separate marketing landing page.
- Per-user configurable demo document sets.
- An automated GIF/screencast pipeline (a README demo pointer is in scope; capturing the GIF is manual).
