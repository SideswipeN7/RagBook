# Feature Specification: Brak podstaw do odpowiedzi — „nie znalazłem w dokumentach" (US-17)

**Feature Branch**: `013-us17-no-basis`

**Created**: 2026-07-13

**Status**: Draft

**Input**: User description: US-17 — a trustworthy "no basis" answer. When the user's documents don't contain the answer, the assistant returns an unambiguous *"Nie znalazłem tego w dokumentach"* instead of a fabricated answer, rendered as a neutral message state (not an error), so every substantive answer can be trusted.

## Context

Grounding already has **two intended lines of defence**; the first is built, the second is the heart of this feature:

1. **Deterministic (pre-LLM) — already shipped in US-14.** When no retrieved passage clears the similarity threshold, the ask pipeline reports the question is not answerable and the backend streams a terminal "no grounds" signal **without calling the model** (verifiable by a mock: the generator is never invoked) and with **no source list**. US-17 keeps this path and unifies how its state is presented.
2. **Prompt-driven (in-LLM) — this feature.** Passages cleared the threshold but do **not** contain the answer, so the grounding prompt (US-14) obliges the model to reply with an exact **refusal sentinel** phrase. US-17 **detects** that sentinel in the generated answer and maps the message to a distinct **NoAnswerFound** state.

The message state is **metadata**, not just text: the UI renders NoAnswerFound **neutrally** (informational icon/colour) — clearly different from a technical error (US-19) and from a produced answer — and offers next-step hints. A **partial** answer (documents cover part of the question) stays a **Normal** answer.

## Clarifications

### Session 2026-07-13

- Q: How should the completed-message signal convey NoAnswerFound, and should the deterministic off-topic cut-off (US-14) and the prompt-refusal share one state? → A: Extend the terminal `done` payload **additively** with `state: answered | no_answer` (keep `groundsFound` for back-compat). **Both** the deterministic no-grounds path and the prompt-refusal map to `no_answer` — a **single** NoAnswerFound UI state. The frontend shows the searched-fragments section only when a `sources` event was received (prompt-refusal). No event rename/reorder.
- Q: For the deterministic pre-LLM cut-off (off-topic, no passages retrieved), what does the NoAnswerFound message show? → A: The same neutral message + next-step hints, but **no** "przeszukane fragmenty" section (there were no passages); the searched-fragments section appears only in the prompt-refusal path where passages exist.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Prompt refusal becomes a trustworthy "not found" (Priority: P1) 🎯 MVP

The documents are on-topic but don't actually answer the question. The model returns the refusal sentinel; the user sees a clear, neutral *"Nie znalazłem tego w dokumentach"* — never an invented answer.

**Why this priority**: This is the core trust guarantee of the whole product — without it, a plausible-sounding hallucination is indistinguishable from a grounded answer. It is the "hallucinations" story told in an interview.

**Independent Test**: Ask a question whose retrieved passages clear the threshold but lack the answer (fake generator scripted to emit the sentinel) → the completed message carries the NoAnswerFound state (not a normal answer, not an error) and shows the neutral "not found" rendering.

**Acceptance Scenarios**:

1. **Given** passages that pass the threshold but don't answer the question, **When** the model replies with the exact refusal sentinel, **Then** the backend marks the completed message **NoAnswerFound** and the UI shows the neutral "not found" state plus a collapsible **"przeszukane fragmenty"** section (transparency).
2. **Given** a NoAnswerFound message, **When** it is rendered, **Then** it does **not** appear as a produced answer (no answer paragraph presented as a real answer) and carries no citation list of "used" sources.

---

### User Story 2 - Distinct from a technical error, with next steps (Priority: P1)

A "not found" is not a failure — the user should see a calm, informational message that tells them what to try next, visually distinct from the red error state.

**Why this priority**: Miscommunicating "no basis" as an error erodes trust and hides the (legitimate) actions that would help — broaden the scope, wait for a document to finish processing, or rephrase.

**Independent Test**: Render a NoAnswerFound message and an Error message side by side → the NoAnswerFound uses an informational (non-error) treatment and lists the next-step hints; the error uses the error treatment with Try-again.

**Acceptance Scenarios**:

1. **Given** a NoAnswerFound message, **When** rendered, **Then** it uses an informational (not error) style and shows next-step hints: broaden the scope, check the document is Ready, rephrase the question.
2. **Given** a technical error (US-19) and a NoAnswerFound in the same thread, **When** both are rendered, **Then** they are visually and semantically distinct (different treatment; error offers Try-again, NoAnswerFound offers hints).

---

### User Story 3 - Partial answers stay normal (Priority: P2)

When documents answer one part of a two-part question, the assistant answers the covered part (with citations) and explicitly names what's missing — this is a **normal**, trustworthy answer, not a refusal.

**Why this priority**: Over-refusing (treating any gap as "not found") would throw away genuinely useful partial answers; the sentinel must be an all-or-nothing refusal, not a substring.

**Independent Test**: Script the generator to return a substantive answer that contains citations and also mentions a gap (and, as an edge case, contains the sentinel phrase mid-text) → the message stays **Normal** and renders as an answer with citations.

**Acceptance Scenarios**:

1. **Given** a two-part question the documents partly cover, **When** the model answers the covered part with `[n]` citations and names the missing part, **Then** the message state is **Normal** (not NoAnswerFound).
2. **Given** an answer that merely *contains* the sentinel phrase mid-text (not as its opening/whole content), **When** completed, **Then** it is treated as a **Normal** answer.

---

### User Story 4 - Deterministic off-topic cut-off is presented consistently (Priority: P2)

A question unrelated to the documents is cut off deterministically before the model is ever called; its state is presented consistently with the prompt-refusal case.

**Why this priority**: AC-1 behaviour already exists (US-14); US-17's job is to make the *presentation* of the pre-LLM cut-off and the in-LLM refusal coherent, and to lock the pre-LLM path with tests.

**Independent Test**: Ask an off-topic question (no passage clears the threshold) → a terminal "no grounds" state arrives immediately, the generator is **never** invoked (mock), and no source list is shown.

**Acceptance Scenarios**:

1. **Given** a question unrelated to any indexed content, **When** retrieval returns nothing above the threshold, **Then** a terminal no-grounds state is produced **without** calling the model and **without** a source list.
2. **Given** the deterministic no-grounds path and the prompt-refusal path, **When** each completes, **Then** both resolve to the single NoAnswerFound state (`done.state = no_answer`) and present a coherent "not found" experience — the deterministic one without a searched-fragments section, the prompt-refusal one with it.

---

### User Story 5 - The threshold is testable via an evaluation set (Priority: P2)

The chosen threshold behaviour is pinned by a small evaluation set so it can't silently regress.

**Why this priority**: A grounding threshold with no regression net is a latent hallucination risk; a documented eval set makes the cut-off a tested, explainable decision.

**Independent Test**: An integration test seeds demo documents and runs ≥10 question→expected-state pairs through the real pipeline (fake generator, no real Anthropic); off-topic questions land on the deterministic path.

**Acceptance Scenarios**:

1. **Given** a seeded document set and ≥10 (question, expected state) pairs, **When** the pipeline runs each, **Then** every pair yields its expected state and off-topic questions hit the deterministic (no-LLM) path.
2. **Given** the eval set, **When** the suite runs, **Then** no case calls the real model provider, and the chosen threshold value is documented.

### Edge Cases

- **Sentinel mid-answer**: the refusal phrase appears inside a longer, otherwise-substantive answer → treated as a **Normal** answer (the sentinel counts only as the whole, trimmed answer — equality).
- **Sentinel appears only after several streamed tokens**: a brief flash of the sentinel text before the message switches to the NoAnswerFound state is acceptable and noted; buffering the stream start to avoid the flash is out of scope (future work).
- **Minor sentinel variation** (surrounding whitespace / trailing punctuation): matching normalises leading/trailing whitespace before comparing (a trimmed comparison), so incidental whitespace still maps to NoAnswerFound; wording differences do not.
- **Refusal with no retrieved passages that cleared the threshold** cannot occur in the prompt-refusal path (that path only runs when passages were sent); the deterministic path covers the empty case.
- **NoAnswerFound never carries a technical error code** and never offers Try-again (those belong to US-19).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST detect when a completed grounded answer **is** the refusal sentinel (`GroundingPrompt.RefusalPhrase`), matching it as the **whole** answer after trimming leading/trailing whitespace (equality, not a substring/prefix match — the prompt requires "exactly this sentence and nothing else").
- **FR-002**: On detecting the sentinel, the system MUST mark the completed message with a distinct **NoAnswerFound** state, separate from a produced (Normal) answer and from a technical error. The **deterministic** pre-LLM no-grounds path and the **prompt-refusal** path both resolve to the **single** NoAnswerFound state (they need not carry separate causes in the contract).
- **FR-003**: The system MUST NOT change the names or order of the existing stream events (`sources` → `token`s → `done` / `error`). The NoAnswerFound state MUST be conveyed by **additively** extending the terminal `done` payload with an explicit `state` value (`answered` | `no_answer`), preserving the existing `groundsFound` field for back-compat; both no-grounds paths emit `state: no_answer`.
- **FR-004**: The deterministic pre-LLM no-grounds path (US-14) MUST remain: when nothing clears the threshold, a terminal no-grounds state is produced **without** invoking the model and with **no** source list.
- **FR-005**: A **partial** answer (covers part of the question, may name the missing part, may contain the sentinel phrase mid-text) MUST remain a **Normal** answer.
- **FR-006**: The UI MUST render a NoAnswerFound message **neutrally** (informational treatment), clearly distinct from a technical error, with the text *"Nie znalazłem tego w dokumentach"* and next-step hints: broaden the scope, check the document is Ready, rephrase the question.
- **FR-007**: When the NoAnswerFound came from the **prompt-refusal** path (passages were in context, i.e. a `sources` event was received), the message MUST offer a collapsible **"przeszukane fragmenty"** section listing those passages (transparency), reusing the existing searched-sources presentation, and MUST NOT present a "used sources" citation list or a produced answer. When it came from the **deterministic** path (no passages), the message MUST show only the neutral text + hints, with **no** searched-fragments section.
- **FR-008**: A NoAnswerFound message MUST NOT show error affordances (no error styling, no Try-again).
- **FR-009**: The chosen similarity threshold value MUST be documented, and its off-topic cut-off behaviour MUST be pinned by an evaluation set of ≥10 (question, expected state) pairs run through the real pipeline against seeded documents, with **no** call to the real model provider.
- **FR-010**: Sentinel detection over a streamed answer MUST accumulate the streamed text and evaluate the sentinel on completion; a brief on-screen flash of sentinel text before switching to the NoAnswerFound state is acceptable (no stream-start buffering required).

### Key Entities

- **Message state**: the metadata classifying a completed chat message — **Normal** (a produced answer, possibly partial, with citations), **NoAnswerFound** (grounded refusal — deterministic or prompt-driven), **Interrupted** (US-15), **Error** (US-19). Drives how the message is rendered.
- **Refusal sentinel**: the fixed phrase the grounding prompt requires for an in-LLM refusal (`GroundingPrompt.RefusalPhrase`, defined in US-14); the detection key for NoAnswerFound.
- **Searched passages**: the numbered passages placed in the prompt context (US-14/16); shown under "przeszukane fragmenty" even when the answer is NoAnswerFound.
- **Evaluation pair**: a (question, expected message state) case over seeded demo documents, used to pin threshold/refusal behaviour in an integration test.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of answers that are exactly the refusal sentinel (after trimming) are presented as NoAnswerFound, and 0% are presented as a produced answer.
- **SC-002**: A NoAnswerFound message is visibly distinguishable from a technical-error message and from a produced answer in 100% of renders (distinct treatment + hints vs. Try-again).
- **SC-003**: Off-topic questions (nothing over threshold) reach the user as "not found" **without any model call** in 100% of cases (mock-verified).
- **SC-004**: An evaluation set of ≥10 (question, expected state) pairs passes in the integration suite with no real-provider calls, and the threshold value is recorded in project docs.
- **SC-005**: A partial answer that names a gap (and/or contains the sentinel phrase mid-text) is presented as a Normal answer with its citations in 100% of such cases.

## Assumptions

- The refusal sentinel and grounding system instructions are already defined and enforced in the US-14 prompt (`GroundingPrompt.RefusalPhrase` + system instructions); US-17 consumes them and does not reword them.
- The deterministic pre-LLM no-grounds path, the SSE event names/order, the streaming chat render (US-15), and the sources / "searched fragments" presentation (US-16) exist on master and are reused.
- Tests never call the real model provider; a scriptable fake generator drives sentinel / partial / normal cases, and the deterministic path is asserted via the generator-not-invoked mock.
- The sentinel is matched after trimming surrounding whitespace (not a looser fuzzy match); wording variations from the model are treated as Normal answers, accepting that a mis-worded refusal would render as a (citation-less) normal answer rather than NoAnswerFound.
- Persisting conversation history across reloads and re-resolving deleted-document citations are **US-18**; automatic scope widening, question suggestions, LLM-as-judge evaluation, and stream-start buffering are out of scope.
