# Phase 0 Research — US-17 No-basis / refusal sentinel

## D1 — Where does refusal detection live?

**Decision**: A pure domain rule `GroundingPrompt.IsRefusal(string answer)` in `Modules/Chat/Domain`, called by
the streaming endpoint after the answer is fully accumulated.

**Rationale**: The sentinel phrase already lives on `GroundingPrompt` (US-14); the "is this answer a refusal?"
rule belongs beside it, not buried in transport code. As a pure function it gets a cheap Domain-tier test
(Constitution §IV) and keeps `ChatEndpoints` a thin projection.

**Alternatives rejected**: Inline `answer.Trim() == RefusalPhrase` in the endpoint (untestable at the cheap tier,
scatters the rule); a new `IRefusalDetector` service (over-engineered for a pure string rule, needs DI wiring).

## D2 — Match semantics (whole/opening vs contains; whitespace; case)

**Decision**: `IsRefusal` = **trimmed ordinal equality** — `answer.Trim().Equals(RefusalPhrase, StringComparison.Ordinal)`.

**Rationale**: The grounding prompt instructs the model to reply "with EXACTLY this sentence and nothing else",
so the refusal is the **whole** message. Trimming absorbs incidental leading/trailing whitespace (edge case);
ordinal (case-sensitive) avoids culture surprises and matches the fixed Polish phrase. Equality automatically
satisfies the spec's rules: an answer that merely *contains* the sentinel is longer than it → not equal →
**Normal** (FR-005); a partial answer with citations is not equal → Normal.

**Alternatives rejected**: `StartsWith` (would misclassify an answer that opens with the phrase then continues —
contradicts "and nothing else"); `Contains` (would over-refuse partial answers — the exact failure mode US-17
guards against); case-insensitive / fuzzy (out of scope; a mis-worded refusal is accepted as a citation-less
Normal answer per the spec assumption).

## D3 — SSE contract shape for NoAnswerFound (clarify Q1)

**Decision**: Extend the terminal `done` payload **additively** to `{ groundsFound: bool, state: "answered" |
"no_answer" }`. Keep `groundsFound` unchanged for back-compat. Both no-grounds paths emit `state: "no_answer"`:
- Deterministic (`StreamInsufficientAsync`): `done { groundsFound: false, state: "no_answer" }` — no `sources`
  event, no `token`s (unchanged except the added field).
- Prompt-refusal: normal `sources` + `token`s stream, then `done { groundsFound: true, state: "no_answer" }`.
- Normal answer: `done { groundsFound: true, state: "answered" }`.

**Rationale**: A single explicit `state` makes the frontend logic intention-revealing and future-proof, without
overloading `groundsFound` (which conflates "had grounds" with "produced an answer"). Additive → no event
rename/reorder (FR-003), existing `done`-consumers keep working. The frontend distinguishes the two no-answer
sub-cases purely by whether a `sources` event was received — no extra field needed (clarify Q2).

**Alternatives rejected**: Reusing `groundsFound:false` for both (overloads the flag; the prompt-refusal genuinely
*had* grounds); a brand-new event type (violates "no event rename/reorder"; more frontend plumbing); two distinct
states (the UI renders them identically save for the fragments section, which is already derivable from `sources`
presence).

## D4 — Streaming: when to classify

**Decision**: Stream `token`s as today; **accumulate** them into a `StringBuilder`; classify on completion and set
the `done.state` accordingly. Accept a brief on-screen flash of the sentinel text before the frontend switches the
message to the neutral state (FR-010).

**Rationale**: The sentinel is only known once the whole answer is in hand; the model emits it as a short single
message so the flash is minimal. Buffering the stream start to hide the flash is explicitly future work (out of
scope) and would add latency/complexity to the hot path.

**Alternatives rejected**: Buffer the first N tokens before emitting any (adds latency, complicates the writer,
out of scope); detect incrementally with a prefix-match state machine (needless — equality on completion is
simpler and correct).

## D5 — Frontend state + render variant

**Decision**: Add `'no_answer'` to `ChatExchange.status`. The `done` handler sets `status = state === 'no_answer'
? 'no_answer' : 'complete'`. Render a **neutral** NoAnswerFound variant (informational tokens — soft surface +
body/muted text, never `--color-error`) with the fixed text "Nie znalazłem tego w dokumentach" and next-step
hints (broaden scope / check the document is Ready / rephrase). Reuse US-16's searched-sources list + preview for
the „przeszukane fragmenty" section, shown **only** when `sources.length > 0` (prompt-refusal). Remove the ad-hoc
`groundsFound`-driven note.

**Rationale**: A first-class status keeps the template branches explicit and the styling distinct from error
(FR-006/FR-008, SC-002). Reusing the US-16 searched-list avoids duplicating citation/preview code.

**Component boundary** (finalised in tasks): extend `chat-answer` (it already owns sources + preview) to render
the neutral block for `no_answer`, or add a small `no-answer` presentational component and keep `chat-answer` for
Normal. Leaning toward extending `chat-answer` to avoid re-plumbing the searched-list + preview.

## D6 — Evaluation set design (AC-5 / FR-009)

**Decision**: An integration `[Theory]` (`NoBasisEvalTests`) seeds a small demo corpus and drives ≥10 (question,
expected state) rows through the **real** pipeline with the fake generator:
- **Off-topic** rows → nothing clears `RagOptions.SimilarityThreshold` → deterministic `no_answer`, generator
  **not invoked** (asserted via `factory.Generator.Invoked == false`), no `sources`.
- **Refusal** rows → passages clear the threshold; the generator is scripted to emit `GroundingPrompt.RefusalPhrase`
  → `done.state = no_answer` with a `sources` event.
- **Answered** rows → generator scripted with a normal `[n]`-citing answer → `done.state = answered`.

The chosen `SimilarityThreshold` (0.75, from `RagOptions`) is documented in `README` ("Grounding i odmowa
odpowiedzi") and here.

**Rationale**: Exercises the deterministic classifier (the real risk surface) end-to-end while keeping the model
faked (§V). Per-row the theory sets `factory.Generator.Deltas`/`Reset()` before asking (xUnit runs a class
fixture's theory cases sequentially, so the shared generator is safe to reconfigure per row).

**Alternatives rejected**: LLM-as-judge evaluation (explicitly out of scope); asserting exact distances (brittle;
the state is the contract, not the score).

## D7 — Backend state representation

**Decision**: Represent the two wire states as a tiny internal enum/const on the Chat side (e.g. `AnswerState`
with serialized values `answered`/`no_answer`) to avoid magic strings in the endpoint; no persisted `MessageState`
entity (no message store until US-18). The frontend mirrors the two values in its status union.

**Rationale**: Config/§VII "no magic numbers/strings" hygiene without introducing persistence the MVP doesn't have
yet. The richer `MessageState { Normal, NoAnswerFound, Interrupted, Error }` from the source doc is realised as the
frontend `status` union; the backend only needs the two terminal wire values.
