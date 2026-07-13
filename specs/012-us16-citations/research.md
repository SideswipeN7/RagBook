# Phase 0 — Research & Decisions: US-16 citations

Grounded in US-14 (`GroundingPassage`, `SourceDto`, `PromptBuilder`, the `sources` event), US-13 (`RetrievedChunk{ChunkId,Text,PageNumber,FileName}`), and US-15 (`ChatStore` thread + `Source` + chat render).

## D1 — Extend `sources` additively (chunk id + full text)

- **Decision**: `GroundingPassage` gains **`ChunkId`** (already has `Text`); `PromptBuilder` passes `RetrievedChunk.ChunkId`. `SourceDto` gains **`Text`** (full chunk) and **`ChunkId`**; the `ChatEndpoints` `sources` projection sends them. Event names/order and all other fields are unchanged.
- **Rationale**: The `[n]`→passage mapping must be **deterministic data** (US-16 FR-002): the backend already numbers the passages when it builds the prompt, so it simply ships `text`+`chunkId` alongside the existing number/document/page. Additive → no US-14/US-15 breakage.
- **Alternatives rejected**: parsing the model's output on the backend to build citations (non-deterministic, defeats the guarantee); a separate "fetch chunk text" endpoint for the preview (an extra round-trip + the chunk may be deleted — the text must travel with the answer, FR-008).

## D2 — Snippet derived on the client (one text field)

- **Decision**: The `sources` payload carries the **full `text`** only; the list **snippet** is derived on the client by truncating `text` (≈ 200 chars + ellipsis). No separate `snippet` field, no server config.
- **Rationale**: Full text is already sent for the preview (FR-004), so a server snippet is redundant. Truncating client-side keeps the payload minimal and the "how long is a snippet" a pure display concern. Chunks are bounded (US-06), so sending full text for ≤ TopK passages is a modest payload.
- **Alternatives rejected**: a separate server `snippet` field (redundant with `text`) + a `RagOptions.SnippetChars` (needless config for a display truncation).

## D3 — Parse `[n]` on the client, at render

- **Decision**: A pure `citation-parser` turns the answer string + the set of valid numbers (`1..K` from the exchange's sources) into an ordered list of segments: `{type:'text', value}` and `{type:'citation', number}`. In-range `[n]` → a citation segment (clickable); an **out-of-range** `[n]` (n ∉ sources) → left as a **text** segment and a `console.warn` quality signal (FR-006). Repeated/adjacent markers each become their own citation segment; text between/around markers is preserved (FR: markers never break text). The `chat-answer` component renders segments (text spans + citation buttons).
- **Rationale**: The client holds the full streamed answer, so it is the natural place to map markers to the exchange's sources — no backend answer-buffering needed. A pure parser is unit-testable (edge cases: mid-sentence, `[1][2]`, repeated, out-of-range).
- **Alternatives rejected**: backend regex over the buffered answer (the backend streams, does not buffer; and US-18 owns any persisted parse); a markdown pipeline (overkill — the answer is plain text with `[n]` markers for MVP).

## D4 — Used vs "other searched fragments"

- **Decision**: A passage is **used** iff its number appears in the completed answer (the citation segments' numbers). The `chat-answer` component shows used sources (file, page, client-snippet) inline, and the remaining retrieved passages under a **collapsible "pozostałe przeszukane fragmenty"** (`<details>`-style, closed by default). If the answer used **no** markers but has passages, all are shown with a "żaden fragment nie został wprost zacytowany" note + a warning (FR-007). Computed at completion (the used set is stable once `done`).
- **Rationale**: FR-005/SC-004 — highlight the evidence the answer stood on, keep the rest available but out of the way.

## D5 — In-app passage preview

- **Decision**: `chat-answer` holds an `openSource` signal (the passage being previewed, per exchange). Clicking a `[n]` citation or a source entry sets it; a **panel/overlay** (design tokens, dismissible, no native dialog) shows the passage's **full `text`** + file name + page. Because the text is stored on the exchange, the preview works even after the source document is deleted in-session (FR-008); a "document deleted" banner + cross-reload persistence are US-18.
- **Rationale**: FR-004 + AC-2/AC-4. The preview reads captured data, not a live chunk.

## D6 — No sources for a no-basis answer

- **Decision**: When an exchange completed with `groundsFound:false`, `chat-answer` renders **no** used-source list (US-15 already shows the neutral no-basis note; sources are empty). FR-009.
- **Rationale**: A refusal has nothing to cite.

## Open items deferred (not blocking)

- Original-PDF preview with page navigation/highlighting; answer export with a bibliography → out of scope.
- Cross-reload persistence + re-resolving a deleted-document citation to a "document deleted" banner → **US-18**.
- Refusal sentinel detection / full no-basis UX → **US-17**.
- Backend quality-warning observability (out-of-range/no-marker at the server) → could join US-19; US-16 warns client-side.
