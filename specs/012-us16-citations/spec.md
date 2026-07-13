# Feature Specification: Cytaty źródeł — klikalne, weryfikowalne (US-16)

**Feature Branch**: `012-us16-citations`

**Created**: 2026-07-12

**Status**: Draft

**Input**: User description: "US-16 — Cytaty źródeł. Backend wzbogaca zdarzenie `sources` (snippet + pełny tekst chunka + chunkId); frontend renderuje znaczniki `[n]` jako klikalne odnośniki + listę źródeł (użyte / pozostałe przeszukane) + podgląd fragmentu. Cytaty deterministyczne — mapowanie n→chunk z danych promptu, nie zgadywanie modelu."

## Boundary note (US-16 vs US-14/15/17/18)

The grounded answer + streaming (US-14) and the chat UI + source list (US-15) already exist. US-16 makes the answer **verifiable**: the backend enriches the `sources` event with each passage's **snippet + full chunk text + chunk id** (the `[n]`→passage mapping is **deterministic** — the backend built the prompt, so it is data, not a guess parsed from the model), and the frontend turns `[n]` markers in the answer into **clickable citations** that open a passage **preview**, splits sources into **used** vs a collapsed **"other searched fragments"** section. The original-PDF preview with page navigation/highlighting is **out of scope** (we show the chunk text, not a position in the PDF); cross-reload persistence + re-resolving a deleted-document citation is **US-18**; the refusal sentinel detection is **US-17**. The SSE event names/order (`sources`/`token`/`done`/`error`) are unchanged.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - See the sources behind an answer (Priority: P1)

After an answer completes, the user sees, beneath it, the list of sources — file name, page (for PDFs), and a short snippet — with the passages the answer **actually cited** highlighted, and the remaining retrieved passages tucked into a collapsible "other searched fragments" section.

**Why this priority**: Verifiability is the core value proposition — the user must see what the answer stands on.

**Independent Test**: Complete an answer whose text contains `[1]`; the source list shows source 1 as used (with file/page/snippet) and any other retrieved passages under a collapsed "other searched fragments" section.

**Acceptance Scenarios**:

1. **Given** a completed answer that used retrieved passages, **When** the `sources` are received, **Then** a list appears under the answer with each source's file name, page number (PDF), and snippet.
2. **Given** the answer text contains some `[n]` markers, **When** the list renders, **Then** only the passages whose number appears in the text are shown as **used/highlighted**; the rest are in a collapsible **"pozostałe przeszukane fragmenty"** section.

---

### User Story 2 - Click a citation to verify it (Priority: P1)

The `[n]` markers in the answer are clickable; clicking one opens a preview of that passage — its full chunk text plus the source file and page — so the user can check the claim.

**Why this priority**: The click-through from claim to evidence is what makes the answer trustworthy and is the project's headline demo.

**Independent Test**: With an answer containing `[2]`, click `[2]`; a preview opens showing passage 2's full text, file name, and page.

**Acceptance Scenarios**:

1. **Given** an answer with a `[2]` marker, **When** the user clicks `[2]`, **Then** a preview opens with passage 2's full chunk text, file name, and page.
2. **Given** the preview is open, **When** the user dismisses it, **Then** it closes without a native browser dialog (uses an in-app panel).

---

### User Story 3 - Citations map deterministically (Priority: P1)

Every `[n]` the answer uses maps to the exact passage that was placed in the prompt as number `n` — the mapping comes from the prompt data, never from guessing the model's intent.

**Why this priority**: A wrong or fabricated citation is worse than none; the mapping must be exact and auditable.

**Independent Test**: For a generated answer, each `[n]` marker resolves to the passage the prompt numbered `n` (by chunk id), verified against the prompt data.

**Acceptance Scenarios**:

1. **Given** the prompt numbered passages `[1..K]`, **When** the answer references `[n]`, **Then** the citation resolves to exactly that passage (same chunk id), from the data — not by re-interpreting the model.
2. **Given** a marker `[n]` **outside** `1..K` (the model hallucinated a number), **When** the answer renders, **Then** that marker stays **plain text** (no link) and a quality warning is recorded.

---

### User Story 4 - Citations survive their document being deleted (Priority: P2)

A citation's preview shows the passage text captured with the answer, so it still works even if the source document is later deleted (the citation does not depend on the chunk still existing).

**Why this priority**: An answer is a record; its evidence should not vanish when a document is removed. (Full cross-reload persistence + a "document deleted" banner is US-18.)

**Independent Test**: Complete an answer; delete the cited document; click the citation — the preview still shows the captured passage text.

**Acceptance Scenarios**:

1. **Given** a completed answer citing a document, **When** that document is later deleted, **Then** clicking the citation still shows the captured passage text (the preview reads the answer's stored data, not a live chunk).

---

### User Story 5 - No sources for a no-basis answer (Priority: P2)

When the answer has no grounds (the "no basis" path), no used-source list is shown (at most the "searched fragments" section is empty/absent).

**Why this priority**: A refusal has nothing to cite; showing citations would mislead.

**Independent Test**: Complete a `groundsFound:false` answer; no used-source list is rendered.

**Acceptance Scenarios**:

1. **Given** a completed answer reporting no grounds, **When** it renders, **Then** no used-source list appears (the sources are empty or only the collapsed searched-fragments section, which is empty).

---

### Edge Cases

- **Marker out of range** (`[n]`, `n` ∉ `1..K`) → rendered as plain text (no link); a quality warning is recorded.
- **No markers despite a substantive answer** → the source list shows all retrieved passages with a note that none were explicitly cited; a quality warning is recorded.
- **Marker mid-sentence / in a list** → the renderer replaces only the marker, without breaking the surrounding text.
- **Repeated / adjacent markers** (`[1][2]`, or `[1]` used several times) → every occurrence is clickable; each resolves to its one passage.
- **A used source with no page** (TXT/MD) → the source shows without a page number.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The `sources` payload MUST carry, per passage, its **number**, **document id**, **file name**, **page** (nullable), the **full chunk text**, and the **chunk id** — enough for a full preview and a list entry. The list **snippet is derived on the client** by truncating the full text (no separate server `snippet` field — it would be redundant with the text).
- **FR-002**: The `[n]`→passage mapping MUST be **deterministic**, derived from the prompt data the backend built (by chunk number/id), never inferred from the model's output.
- **FR-003**: The answer renderer MUST turn each in-text `[n]` marker into a **clickable citation** that opens the preview of passage `n`, replacing only the marker and leaving the surrounding text intact.
- **FR-004**: Clicking a citation MUST open an **in-app preview** (not a native dialog) showing the passage's **full chunk text**, file name, and page; the user can dismiss it.
- **FR-005**: Beneath a completed answer, the system MUST list its sources: passages whose number appears in the answer are shown as **used** (file, page, snippet); the remaining retrieved passages are in a **collapsible "other searched fragments"** section.
- **FR-006**: A marker `[n]` with `n` outside the retrieved set MUST render as **plain text** (no link), and the system MUST record a **quality warning** (observability), not fail.
- **FR-007**: An answer that uses **no** markers despite having retrieved passages MUST show the list with all passages and a note that none were explicitly cited, and record a quality warning.
- **FR-008**: A citation's preview MUST read from the **answer's captured data** (snippet/text stored with the exchange), so it still works after the source document is deleted (in-session). Cross-reload persistence is US-18.
- **FR-009**: For a **no-basis** answer, the system MUST NOT render a used-source list (sources are empty / only an empty searched-fragments section).

### Key Entities *(include if feature involves data)*

- **Cited passage (source)**: the evidence for a `[n]` reference — its number, document (id + file name), page, snippet, full text, and chunk id. Carried in the `sources` event and stored on the answer.
- **Citation marker**: an in-text `[n]` reference; resolves to the cited passage numbered `n`, or renders as plain text when out of range.
- **Passage preview**: the in-app view of a cited passage's full text + source metadata, opened from a citation or the source list.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Every completed grounded answer shows a source list with file name, page (PDF), and snippet for each retrieved passage — 100% of grounded answers.
- **SC-002**: Every in-range `[n]` marker is clickable and opens the correct passage's preview (full text + file + page) — 100% of in-range markers.
- **SC-003**: Each `[n]` resolves to exactly the passage numbered `n` in the prompt (by chunk id) — **0** mis-mappings.
- **SC-004**: Passages the answer cited are shown as used; uncited retrieved passages are collapsed — 100% of answers separate the two correctly.
- **SC-005**: An out-of-range marker never links and never breaks rendering; a quality warning is recorded — 100% of such cases.
- **SC-006**: A citation preview still shows its passage after the source document is deleted (in-session) — 100%.

## Assumptions

- Reuses US-14's `sources` event and US-15's chat thread/source list; US-16 **extends** the payload and the renderer, without changing event names/order.
- The `sources` payload carries the **full chunk text** (modest — chunks are bounded, ≤ TopK passages); the list **snippet is derived on the client** by truncating that text (≈ 200 characters). No separate server snippet field.
- "Used" passages are determined by scanning the completed answer text for `[n]` markers; this happens where the full answer text is available (the client, which holds the streamed answer).
- The preview is an in-app panel/overlay (no native dialog, per the design system); highlighting is at the passage level (the whole chunk), not a position within the original PDF.
- Quality warnings (out-of-range / no-marker) are recorded for observability; they do not block or fail the answer.

## Out of Scope

- Original-PDF preview with page navigation and in-document highlighting; exporting an answer with a bibliography.
- Conversation persistence across reloads and re-resolving a deleted-document citation to a "document deleted" banner (**US-18**).
- Refusal sentinel detection / the full "no basis" UX (**US-17**).
- Any change to the SSE event names or order (`sources`/`token`/`done`/`error`).
