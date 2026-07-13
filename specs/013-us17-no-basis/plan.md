# Implementation Plan: Brak podstaw do odpowiedzi — „nie znalazłem w dokumentach" (US-17)

**Branch**: `013-us17-no-basis` | **Date**: 2026-07-13 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/013-us17-no-basis/spec.md`

## Summary

Turn the reserved refusal sentinel into a trustworthy, distinct message state. Two no-grounds paths already
diverge in the pipeline: the **deterministic** pre-LLM cut-off (US-14, no model call) and the **prompt-refusal**
(model returns `GroundingPrompt.RefusalPhrase`). US-17 (a) detects the refusal sentinel on stream completion via
a **domain rule** and (b) conveys a single **NoAnswerFound** state by **additively** adding `state:
"answered" | "no_answer"` to the terminal `done` SSE payload (keeping `groundsFound`), with **no** event
rename/reorder. The frontend renders `no_answer` **neutrally** (informational, not error) with next-step hints and
— only when passages were in context (a `sources` event arrived) — a collapsible „przeszukane fragmenty" reusing
US-16's searched-sources + preview. A partial answer, or an answer that merely contains the sentinel mid-text,
stays **Normal**. An integration **evaluation set** (≥10 question→expected-state pairs on seeded docs) pins the
threshold behaviour; no test calls the real provider.

## Technical Context

**Language/Version**: C# (.NET 10, preview LangVersion) backend; TypeScript / Angular 20 frontend.

**Primary Dependencies**: ASP.NET Core minimal API + manual SSE (US-14/15); Wolverine dispatch; existing
`GroundingPrompt`, `IAnswerGenerator`, `IAskQuestionPipeline`; Angular standalone/OnPush/signals; design tokens.

**Storage**: PostgreSQL + pgvector (unchanged; no schema change — no message persistence until US-18).

**Testing**: xUnit + NSubstitute + FluentAssertions (Domain/Application/Integration tiers), Testcontainers
`pgvector/pgvector:pg17` for Integration; Karma/ChromeHeadless for Angular.

**Target Platform**: Linux container (GCP Cloud Run) backend; evergreen browser SPA.

**Project Type**: Web (modular-monolith .NET backend + Angular SPA).

**Performance Goals**: The deterministic path stays a **no-model-call** immediate response; sentinel detection is
an O(n) trim+compare over the accumulated answer — negligible.

**Constraints**: No SSE event rename/reorder (additive `done` field only); no real-provider calls in tests; design
tokens only, no native dialogs, ≥360px; brief sentinel flash before state switch is acceptable (no stream-start
buffering).

**Scale/Scope**: Small, additive slice — one domain rule + a wire field + one frontend render variant + an eval
set. No migration.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **I. Vertical-Slice Modular Monolith** ✅ — all backend work stays in `Modules/Chat` (the refusal rule joins
  `Domain/GroundingPrompt`; the wire projection stays in `RagBook.API/Endpoints`). No new project; no cross-module
  reference.
- **II. CQRS + Result Contract** ✅ — the streaming ask is the established SSE exception to the DTO/Result shape
  (US-14); US-17 only adds a field to an existing event payload. Pre-generation failures remain ProblemDetails.
- **III. Data Isolation** ✅ — retrieval already filters by session; no new query. No entity/column added.
- **IV. Test-First** ✅ — Domain test for the refusal rule (cheapest tier), Integration for the `done.state`
  wire + the eval set (real host, fake generator, happy-path), Angular specs for the render variant. Red→Green.
- **V. Providers — Resilience + Cache** ✅ — no new provider call; the sentinel phrase and similarity threshold
  stay config/`RagOptions`/`GroundingPrompt`-owned (no new magic numbers). No test hits a real service.
- **VI/VII/VIII** ✅ — no time/secret/ops surface touched; no startup migration.
- **IX. Frontend & Design System** ✅ — standalone/OnPush/signals, `input.required`, tokens only (informational
  neutral treatment distinct from `--color-error`), no native dialog, works at ≥360px.

**Result: PASS** — no violations; Complexity Tracking empty.

## Project Structure

### Documentation (this feature)

```text
specs/013-us17-no-basis/
├── plan.md              # This file
├── research.md          # Phase 0 — decisions (detection rule, contract shape, eval design)
├── data-model.md        # Phase 1 — states + wire payload (no DB entities)
├── quickstart.md        # Phase 1 — how to validate US-17 end-to-end
├── contracts/
│   └── no-basis.md      # Phase 1 — the `done` payload + detection contract
├── checklists/
│   └── requirements.md  # Spec quality checklist (from /specify + /clarify)
└── tasks.md             # Phase 2 (/speckit-tasks — NOT created here)
```

### Source Code (repository root)

```text
src/RagBook/Modules/Chat/
├── Domain/
│   └── GroundingPrompt.cs        # + IsRefusal(answer): the domain rule (trimmed-ordinal equality to RefusalPhrase)

src/RagBook.API/Endpoints/
├── ChatEndpoints.cs              # accumulate deltas; emit done{groundsFound,state}; deterministic path → state:no_answer
└── ChatContracts.cs             # (if a done DTO is introduced) DoneDto { groundsFound, state }

src/Web/src/app/
├── core/chat.store.ts           # ChatExchange.status += 'no_answer'; done handler reads `state`
└── chat/
    ├── chat.html                # replace the ad-hoc groundsFound note with the NoAnswerFound render
    ├── chat-answer/…            # render neutral message + hints for no_answer; searched-fragments only if sources present
    └── (optional) no-answer/…   # a small presentational piece for the neutral message + hints (decided in tasks)

tests/
├── RagBook.Domain.Tests/Chat/GroundingPromptTests.cs         # IsRefusal rule (equality/trim/mid-text/partial)
├── RagBook.Application.Tests/…                                # (if the rule needs a handler-level assertion)
└── RagBook.Api.IntegrationTests/Chat/
    ├── AskQuestionEndpointTests.cs   # sentinel → done.state=no_answer (+ sources present); normal → answered
    └── NoBasisEvalTests.cs           # ≥10 (question, expected state) pairs on seeded docs; off-topic → no model call
```

**Structure Decision**: Reuse the existing Chat vertical slice. The **only** new domain surface is a pure
`IsRefusal` rule on `GroundingPrompt` (keeps the sentinel logic with the sentinel, unit-testable at the Domain
tier). The endpoint accumulates the streamed answer and classifies the terminal `done` state. The frontend adds a
`no_answer` status and a neutral render variant; searched-fragments + preview reuse US-16. No DB/migration.

## Complexity Tracking

*No constitution violations — no entries.*
