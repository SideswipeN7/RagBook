# Tasks: Cytaty źródeł — klikalne, weryfikowalne (US-16)

**Input**: Design documents from `specs/012-us16-citations/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/citations.md, quickstart.md

**Tests**: Included — Test-First. Frontend via failing Karma tests (pure parser + component); backend via a
failing integration/unit assertion. No test hits Anthropic (§V).

**Organization**: Additive backend (`sources` payload += `text`/`chunkId`) + a frontend citation layer.
One Setup phase (backend fields), one Foundational phase (parser + store type), then the stories: US1 =
source list (AC-1) 🎯 MVP, US2 = clickable + preview (AC-2), US3 = deterministic + edge markers (AC-3), US4 =
survives deletion (AC-4), US5 = no-basis no list (AC-5).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: parallelizable (different files).
- Paths: `src/RagBook/Modules/Chat`, `src/RagBook.API`, `src/Web/src/app/{core,chat}`, `tests/…`.

---

## Phase 1: Setup — additive backend fields

- [X] T001 [P] Extend the `sources` contract: `GroundingPassage` (`src/RagBook/Modules/Chat/Domain/GroundingPassage.cs`) += `ChunkId`; `PromptBuilder` (`Chat/Domain/PromptBuilder.cs`) passes `RetrievedChunk.ChunkId`; `SourceDto` (`src/RagBook.API/Endpoints/ChatContracts.cs`) += `Text`, `ChunkId`; the `ChatEndpoints` `sources` projection (`ChatEndpoints.cs`) += `passage.Text`, `passage.ChunkId`. (Additive — no event rename/reorder.)
- [X] T002 [P] Application test (Red→Green): extend `PromptBuilderTests` — the built `GroundingPassage` carries the chunk's `ChunkId` (FR-002 deterministic-mapping data) — in `tests/RagBook.Application.Tests/Chat/PromptBuilderTests.cs`.
- [X] T003 [P] Integration test (Red→Green): extend `AskQuestionEndpointTests` — the `sources` event's entries include `text` and `chunkId` (FR-001) — in `tests/RagBook.Api.IntegrationTests/Chat/AskQuestionEndpointTests.cs`.

**Checkpoint**: the `sources` event carries `text` + `chunkId`; PromptBuilder threads `ChunkId`.

---

## Phase 2: Foundational — parser + store type

- [X] T004 [P] Web test (Red): `citation-parser.spec.ts` — `[n]` markers become citation segments and surrounding text is preserved; adjacent `[1][2]`, a repeated `[1]`, and a mid-sentence marker parse correctly; an **out-of-range** `[n]` (n ∉ valid) stays a **text** segment; an **incomplete** marker mid-stream (`[1` before its `]`) stays text until closed (A1) — in `src/Web/src/app/core/citation-parser.spec.ts`.
- [X] T005 Implement pure `citation-parser.ts` — `parseCitations(answer, validNumbers) → Segment[]` (`{type:'text',value}` | `{type:'citation',number}`); an out-of-range marker is left as text and `console.warn`ed (Green for T004) in `src/Web/src/app/core/citation-parser.ts`.
- [X] T006 [P] Extend `ChatStore.Source` (`src/Web/src/app/core/chat.store.ts`) with `text` and `chunkId` (the SSE `sources` JSON now carries them; the existing parse picks them up — just widen the type + a spec assertion that a parsed source exposes `text`/`chunkId`).

**Checkpoint**: parser green; sources carry text/chunkId end-to-end.

---

## Phase 3: User Story 1 — Source list (Priority: P1) 🎯 MVP

**Goal**: Under a completed answer, list its sources; cited passages highlighted, the rest collapsed.

**Independent test**: A completed answer containing `[1]` → source 1 shown (file/page/snippet); other retrieved passages under a collapsed "pozostałe przeszukane fragmenty".

- [X] T007 [US1] Web test (Red): `chat-answer.spec.ts` — given an exchange (answer `"…[1]"` + 2 sources) → source 1 appears as **used** (file, page, client-truncated snippet), source 2 under a collapsed **"pozostałe przeszukane fragmenty"** — in `src/Web/src/app/chat/chat-answer/chat-answer.spec.ts`.
- [X] T008 [US1] Implement `chat-answer` component (`src/Web/src/app/chat/chat-answer/chat-answer.ts|html|scss`, OnPush/signals/tokens): `input.required<ChatExchange>()`; renders the answer as parsed segments + the **used** source list (snippet = truncated `text`) + the collapsible searched section. Wire into `chat.html`: replace the plain answer/sources block with `<app-chat-answer [exchange]="exchange" />` — `chat-answer` owns the **answer + sources + preview**, while `chat.html` keeps the question, status/interrupted/error, and the no-basis note (A2) (Green for T007).

**Checkpoint**: AC-1 — sources listed, used highlighted, rest collapsed. MVP.

---

## Phase 4: User Story 2 — Clickable citation + preview (Priority: P1)

**Goal**: `[n]` in the answer is clickable and opens an in-app preview of passage `n`.

**Independent test**: An answer with `[2]` → clicking `[2]` opens a preview with passage 2's full text + file + page; dismiss closes it.

- [X] T009 [US2] Web test + impl: the answer's in-range `[n]` render as **buttons**; clicking one (or a used-source entry) sets an `openSource` signal and shows an **in-app preview panel** (full `text` + file + page, dismissible, no native dialog) — `chat-answer.spec.ts` + component (AC-2, SC-002).

**Checkpoint**: AC-2 — click a citation → verify the passage.

---

## Phase 5: User Story 3 — Deterministic + edge markers (Priority: P1)

**Goal**: In-range `[n]` maps to exactly its passage; out-of-range/no-marker handled without breaking.

**Independent test**: An out-of-range `[n]` stays plain text + warns; a marker-less answer shows all sources with a note.

- [X] T010 [US3] Web test + impl: an **out-of-range** `[n]` renders as plain text (no link) and logs a warning; a substantive answer with **no** markers shows all retrieved passages with a "żaden fragment nie został wprost zacytowany" note (+ warning) — `chat-answer.spec.ts` (FR-006/FR-007, SC-005). (In-range determinism is carried by T001 `chunkId` + T005 parser.)

**Checkpoint**: AC-3 — exact mapping; hallucinated/absent markers degrade gracefully.

---

## Phase 6: User Story 4 & 5 — Survives deletion + no-basis (Priority: P2)

**Independent test**: preview shows captured text after the doc is gone; a no-basis answer shows no source list.

- [X] T011 [US4] Web test: the preview shows the passage's captured `text` even when the source document no longer exists — the component reads `exchange` data, not a live fetch (SC-006) — in `chat-answer.spec.ts`.
- [X] T012 [US5] Web test + impl: an exchange completed with `groundsFound:false` renders **no** used-source list — `chat-answer.spec.ts` (FR-009, AC-5).

**Checkpoint**: AC-4/AC-5 — citations outlive deletion; no citations for a refusal.

---

## Phase 7: Polish

- [X] T013 [P] Docs: add a **"Cytaty źródeł (US-16)"** section to `README.md` (deterministic `[n]`→chunk from the prompt data; clickable citations → in-app preview; used vs collapsed searched; chunk-text preview, original-PDF nav out of scope) and record durable notes in `AGENTS.md` (`sources` event += `text`/`chunkId`; `citation-parser` + `chat-answer`; client-derived snippet; preview reads captured data so it survives deletion). Note the demo GIF is the headline material.
- [X] T014 Full green run: `npm test` in `src/Web` and `dotnet test` (Application + Testcontainers Integration); then the critical-analysis pass on the diff before opening the PR (repo memory: critical-analysis-before-pr). If Smart App Control blocks local test hosts, push and let CI be the green gate.

---

## Dependencies & execution order

- **Setup (T001–T003)** + **Foundational (T004–T006)** block the stories.
- **US1 (T007–T008)** builds the `chat-answer` render (MVP). **US2 (T009)** adds clickable citations + preview; **US3 (T010)** the edge markers; **US4 (T011)** / **US5 (T012)** assert deletion-survival + no-basis over that component.
- Within a story, tests precede implementation; `[P]` = different files.
- Polish (T013–T014) after the stories are green.

## MVP scope

**US1 (T001–T008)** yields the demonstrable increment: a completed answer shows its sources with the cited
passages highlighted and the rest collapsed. US2–US5 add clickable citations + preview, the deterministic/edge
handling, deletion-survival, and the no-basis case.
