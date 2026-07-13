# Tasks: Brak podstaw do odpowiedzi — „nie znalazłem w dokumentach" (US-17)

**Input**: Design documents from `specs/013-us17-no-basis/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/no-basis.md, quickstart.md

**Tests**: Included — Test-First (Constitution §IV). Domain rule at the cheapest tier; wire state + eval set at
the Integration tier (real host, **fake generator — no real Anthropic**); render variant via Karma.

**Organization**: A pure domain rule + an additive `done.state` wire field + one neutral frontend render variant
+ an evaluation set. US1 = prompt-refusal → NoAnswerFound 🎯 MVP; US2 = distinct-from-error + hints; US3 = partial
stays Normal; US4 = deterministic path consistent; US5 = threshold eval set.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: parallelizable (different files, no ordering dependency).
- Paths: `src/RagBook/Modules/Chat`, `src/RagBook.API/Endpoints`, `src/Web/src/app/{core,chat}`, `tests/…`.

---

## Phase 1: Setup

*No setup tasks — additive slice over existing Chat module; no new project, dependency, or migration.*

---

## Phase 2: Foundational — the refusal domain rule (blocks all stories)

- [X] T001 [P] Domain test (Red): `GroundingPromptTests.IsRefusal` — the exact `RefusalPhrase` → true; the phrase with leading/trailing whitespace → true; the phrase as a mid-text substring of a longer answer → false; a partial answer with `[n]` → false; a normal answer → false — in `tests/RagBook.Domain.Tests/Chat/GroundingPromptTests.cs`.
- [X] T002 Implement `GroundingPrompt.IsRefusal(string answer)` (Green) as trimmed **ordinal equality** to `RefusalPhrase` — in `src/RagBook/Modules/Chat/Domain/GroundingPrompt.cs`.

**Checkpoint**: the refusal rule is defined and unit-proven; the endpoint can classify a completed answer.

---

## Phase 3: User Story 1 — Prompt refusal → NoAnswerFound (Priority: P1) 🎯 MVP

**Goal**: When the model returns the refusal sentinel, the completed message settles into a distinct NoAnswerFound
state rendered neutrally — never presented as a produced answer.

**Independent test**: Script the generator to emit `RefusalPhrase` → `done.state == "no_answer"` (with a `sources`
event) and the UI shows the neutral „nie znaleziono" view, not an answer.

- [X] T003 [US1] Integration test (Red): extend `AskQuestionEndpointTests` — a generator scripted with `GroundingPrompt.RefusalPhrase` yields a terminal `done` whose data has `"state":"no_answer"` and a preceding `sources` event; a normal scripted answer yields `"state":"answered"` — in `tests/RagBook.Api.IntegrationTests/Chat/AskQuestionEndpointTests.cs`.
- [X] T004 [US1] Implement the wire state (Green): in `ChatEndpoints.StreamAnswerAsync` accumulate the streamed deltas and, on normal completion, emit `done { groundsFound, state = GroundingPrompt.IsRefusal(accumulated) ? "no_answer" : "answered" }`; in `StreamInsufficientAsync` emit `done { groundsFound = false, state = "no_answer" }`; introduce internal `AnswerState` constants (`answered`/`no_answer`, no magic strings) — in `src/RagBook.API/Endpoints/ChatEndpoints.cs` (+ a `DoneDto`/payload in `ChatContracts.cs` if a typed shape is cleaner). No event rename/reorder.
- [X] T005 [US1] Frontend store: add `'no_answer'` to `ChatExchange.status`; the `done` handler parses `{ groundsFound, state }` and sets `status = state === 'no_answer' ? 'no_answer' : 'complete'` (retain `groundsFound`) — in `src/Web/src/app/core/chat.store.ts`; extend `chat.store.spec.ts` to assert a `no_answer` `done` yields `status === 'no_answer'`.
- [X] T006 [US1] Frontend render (Red→Green): render a **neutral** NoAnswerFound view for `status === 'no_answer'` — the fixed text "Nie znalazłem tego w dokumentach", **no produced-answer paragraph** (gate the existing `@if(exchange().answer)` paragraph on `status !== 'no_answer'`, since a refusal's accumulated `answer` is the sentinel text) — and wire it into `chat.html`, **replacing** the ad-hoc `status === 'complete' && !groundsFound` note. Owns the render in `src/Web/src/app/chat/chat-answer/` (extend it — it already holds the exchange + sources + preview) or a new `chat/no-answer/` component. Add a `chat-answer.spec.ts` case for the neutral view, and **update the existing `chat.spec.ts` no-basis test** (currently expects "Brak podstaw w wybranym zakresie" for `status:'complete' + groundsFound:false`) to the new `status:'no_answer'` NoAnswerFound render.

**Checkpoint**: AC-2 — a scripted refusal becomes a neutral NoAnswerFound message end-to-end. MVP.

---

## Phase 4: User Story 2 — Distinct from a technical error, with next steps (Priority: P1)

**Goal**: NoAnswerFound reads as calm information with actionable hints — visibly and semantically different from a
red error.

**Independent test**: Render a `no_answer` and an `error` exchange → informational treatment + hints vs. error
treatment + Try-again.

- [X] T007 [US2] Frontend test + styling: the NoAnswerFound view uses **informational** design tokens (soft surface + body/muted, never `--color-error`), shows the three next-step hints (rozszerz zakres / sprawdź czy dokument jest Ready / przeformułuj pytanie), and offers **no** Try-again; a spec asserts it is distinct from the error rendering and carries the hints — in `src/Web/src/app/chat/chat-answer/*` (`.scss` + `.spec.ts`).

**Checkpoint**: AC-3 — NoAnswerFound is unmistakably not an error and guides the next step.

---

## Phase 5: User Story 3 — Partial answers stay Normal (Priority: P2)

**Goal**: An answer that covers part of the question (or merely contains the sentinel mid-text) stays a Normal,
citable answer — no over-refusal.

**Independent test**: Generator scripted with a partial/mid-text-sentinel answer → `done.state == "answered"`,
rendered as a normal answer with citations.

- [X] T008 [US3] Integration test: extend `AskQuestionEndpointTests` — a generator scripted with an answer that **contains** `RefusalPhrase` mid-text (plus other text/citations) yields `"state":"answered"`; a partial two-part answer yields `"state":"answered"` — in `tests/RagBook.Api.IntegrationTests/Chat/AskQuestionEndpointTests.cs`. (Domain equality is already pinned by T001; this proves the endpoint path.)

**Checkpoint**: AC-4 — partial/embedded-sentinel answers remain Normal.

---

## Phase 6: User Story 4 — Deterministic off-topic cut-off, presented consistently (Priority: P2)

**Goal**: The pre-LLM off-topic cut-off resolves to the same NoAnswerFound state, with no model call and no
searched-fragments section.

**Independent test**: Off-topic question → `done.state == "no_answer"`, `Generator.Invoked == false`, no `sources`
event; UI shows message + hints with **no** „przeszukane fragmenty".

- [X] T009 [US4] Integration + frontend: extend the existing deterministic tests (`AskQuestionEndpointTests` `Should_ReturnGroundsFalse…`) to also assert `done.state == "no_answer"` while keeping `Generator.Invoked == false` and no `sources` event — in `tests/RagBook.Api.IntegrationTests/Chat/AskQuestionEndpointTests.cs`; and a `chat-answer.spec.ts` case asserting a `no_answer` exchange with **empty** `sources` renders message + hints and **no** „przeszukane fragmenty" section (present only when `sources.length > 0`).

**Checkpoint**: AC-1 presentation unified — deterministic and prompt refusals share the NoAnswerFound state, differing only by the fragments section.

---

## Phase 7: User Story 5 — Threshold evaluation set (Priority: P2)

**Goal**: Pin the threshold/refusal behaviour with a small, documented eval set so it can't silently regress.

**Independent test**: ≥10 (question, expected state) pairs over seeded docs all resolve to their expected state;
off-topic pairs never call the model.

- [X] T010 [US5] Integration eval (Red→Green): `NoBasisEvalTests` — seed a small demo corpus, then a `[Theory]` of ≥10 (question, expected `state`) rows: **off-topic** rows assert deterministic `no_answer` + `Generator.Invoked == false` + no `sources`; **refusal** rows script the generator with `RefusalPhrase` and assert `no_answer` + a `sources` event; **answered** rows script a normal `[n]`-citing answer and assert `answered`. `Reset()`/reconfigure the generator per row — in `tests/RagBook.Api.IntegrationTests/Chat/NoBasisEvalTests.cs`.
- [X] T011 [US5] Record the chosen `RagOptions.SimilarityThreshold` (0.75) and the off-topic cut-off behaviour in the README section (T012) and confirm it matches `research.md` D6.

**Checkpoint**: AC-5 — threshold behaviour is tested and documented.

---

## Phase 8: Polish

- [X] T012 [P] Docs: add a **„Grounding i odmowa odpowiedzi (US-17)"** section to `README.md` (two lines of defence — deterministic pre-LLM cut-off + prompt sentinel; the `done.state` field; NoAnswerFound rendered neutrally vs. errors with next-step hints; „przeszukane fragmenty" only when passages existed; documented `SimilarityThreshold`), and durable notes in `AGENTS.md` (`done` payload += `state`; `GroundingPrompt.IsRefusal` domain rule = trimmed ordinal equality; `no_answer` frontend status + neutral render; `NoBasisEvalTests` eval set; no event rename/reorder).
- [X] T013 Full green run — `npm test` in `src/Web` and `dotnet test` (Domain + Application + Testcontainers Integration) — then the critical-analysis pass on the diff before opening the PR (repo memory: critical-analysis-before-pr). If Smart App Control blocks local test hosts, push and let CI be the green gate; ensure Docker is up for the Integration tier.

---

## Dependencies & execution order

- **Foundational (T001–T002)** blocks everything (the rule the endpoint calls).
- **US1 (T003–T006)** is the MVP: wire state + store + neutral render. **US2 (T007)** styles/distinguishes it;
  **US3 (T008)** proves partial/embedded-sentinel stays Normal; **US4 (T009)** unifies the deterministic path;
  **US5 (T010–T011)** adds + documents the eval set.
- Within a story, tests precede implementation; `[P]` = different files.
- Polish (T012–T013) after the stories are green.

## MVP scope

**US1 (T001–T006)** delivers the demonstrable increment: a scripted refusal becomes a neutral, trustworthy „nie
znaleziono" message instead of a fabricated answer. US2–US5 add the error-distinction + hints, partial-answer
safety, deterministic-path consistency, and the documented evaluation set.
