# Implementation Plan: Cytaty źródeł — klikalne, weryfikowalne (US-16)

**Branch**: `012-us16-citations` (git: `fm/us16-citations`) | **Date**: 2026-07-12 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/012-us16-citations/spec.md`

## Summary

Make answers **verifiable**. **Backend (additive):** extend the `sources` SSE payload — `GroundingPassage` (US-14) and `SourceDto` gain the **chunk id** and the **full chunk text** (the `[n]`→passage mapping is deterministic prompt data); `PromptBuilder` passes `RetrievedChunk.ChunkId`. **Frontend (the bulk):** a pure `citation-parser` splits the streamed answer into text/citation segments (in-range `[n]` → clickable, out-of-range → plain text + a quality warning); a `chat-answer` component renders those segments, lists **used** sources (number present in the answer) with file/page/**snippet** (derived from `text`), tucks the rest into a collapsible **"pozostałe przeszukane fragmenty"**, and opens an in-app **preview** (full chunk text + file + page) on a citation/source click. Reuses US-15's thread + `ChatStore.Source` (extended with `text`/`chunkId`). No SSE event name/order change, no migration. Original-PDF navigation, cross-reload persistence (US-18), and refusal detection (US-17) are out of scope.

## Technical Context

**Language/Version**: TypeScript / Angular (the bulk); C# (net10.0) for the additive `sources` fields.

**Primary Dependencies**: reuse US-14 `sources` stream + US-15 `ChatStore`/chat thread + US-13 `RetrievedChunk` (already carries `ChunkId`/`Text`). No new package.

**Storage**: none; no migration (citations ride the existing stream; persistence is US-18).

**Testing**: Angular Karma — a pure `citation-parser` spec (segments, out-of-range → plain, adjacent/repeated/mid-sentence markers) + a `chat-answer` component spec (clickable `[n]` opens the preview; used vs collapsed searched; out-of-range plain + no link; no-marker note; preview shows full text after the doc "deleted"; no list for no-basis). Backend — extend the US-14 integration assertion (the `sources` event carries `text` + `chunkId`) + a `PromptBuilder` unit assertion (passage carries `ChunkId`). No test hits Anthropic (§V).

**Target Platform**: Angular SPA; Linux backend.

**Performance Goals**: Segment parsing is O(answer length), re-run cheaply on each render; the full-text `sources` payload is bounded (≤ TopK chunks).

**Constraints**: Deterministic `[n]`→chunk mapping (by number/chunk id from the prompt data). No native dialog (in-app preview, design tokens). Markers never break surrounding text.

**Scale/Scope**: A handful of sources per answer; a modest per-exchange preview.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Assessment |
|---|---|
| I. Vertical-slice modular monolith | ✅ Backend change is additive to the `Chat` module's `GroundingPassage`/`SourceDto` + `PromptBuilder`. No new module. |
| II. CQRS + Result contract | ✅ No new command/query; `SourceDto` is a transport DTO; the SSE contract is unchanged except added fields. |
| III. Data isolation by session | ✅ Reuses US-14/US-13 session scoping; no new data path. |
| IV. Test-First (Red→Green) | ✅ Pure `citation-parser` + `chat-answer` via failing Karma tests; backend field additions via a failing integration/unit assertion. |
| V. External providers — resilience + cache | ✅ No test hits Anthropic (reuses the US-14 fake generator; frontend stubs are unchanged). |
| VI. Auditing & time | ✅ No writes. N/A. |
| VII. Secrets | ✅ Unchanged; no secret handling. |
| VIII. Operations & delivery | ✅ No migration; CI runs all tiers. |
| IX. Frontend & design system | ✅ Standalone + OnPush + Signals; the preview is an **in-app panel** (no native dialog); design tokens (no inline hex); markers rendered without breaking text; ≥360px. |

**No deviations.** No Complexity Tracking entries.

## Project Structure

### Documentation (this feature)

```text
specs/012-us16-citations/
├── plan.md · research.md · data-model.md · quickstart.md
├── contracts/citations.md   # the extended `sources` payload + the client rendering contract
└── tasks.md                 # (/speckit-tasks)
```

### Source Code (repository root)

```text
# Backend (additive)
src/RagBook/Modules/Chat/Domain/GroundingPassage.cs      # ADD: ChunkId (already has Text)
src/RagBook/Modules/Chat/Domain/PromptBuilder.cs         # pass chunk.ChunkId into GroundingPassage
src/RagBook.API/Endpoints/ChatContracts.cs               # SourceDto ADD: Text, ChunkId
src/RagBook.API/Endpoints/ChatEndpoints.cs               # sources projection ADD: passage.Text, passage.ChunkId

# Frontend (the bulk)
src/Web/src/app/core/citation-parser.ts                  # pure: (answer, validNumbers) -> Segment[] ({text} | {citation:n}); out-of-range warned
src/Web/src/app/core/chat.store.ts                       # Source type ADD: text, chunkId
src/Web/src/app/chat/chat-answer/                         # chat-answer.ts/html/scss — segments render (clickable [n]) + used/searched lists + preview panel
src/Web/src/app/chat/chat.html                           # use <app-chat-answer [exchange]="…"/> for the answer + sources (replaces the plain answer/sources block)

# Tests
src/Web/src/app/core/citation-parser.spec.ts
src/Web/src/app/chat/chat-answer/chat-answer.spec.ts
tests/RagBook.Api.IntegrationTests/Chat/AskQuestionEndpointTests.cs   # extend: sources event has text + chunkId
tests/RagBook.Application.Tests/Chat/PromptBuilderTests.cs            # extend: GroundingPassage carries ChunkId
```

**Structure Decision**: Web modular-monolith. US-16 is an **additive** backend change (two fields on the `sources` payload + the chunk id threaded through `PromptBuilder`) plus a **frontend** citation layer: a pure `citation-parser` (unit-tested) feeds a `chat-answer` component that renders clickable `[n]`, the used/searched source split, and an in-app passage preview. The snippet shown in the list is **derived on the client** by truncating the full `text` (so the payload has one text field, not a redundant snippet). The chat thread + `ChatStore` from US-15 are reused; the `Source` type gains `text`/`chunkId`.

## Complexity Tracking

*No entries — additive DTO fields + a frontend citation layer; no new project, no migration, no principle deviation.*
