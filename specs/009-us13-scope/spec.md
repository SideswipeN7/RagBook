# Feature Specification: Zakres pytania — scoped retrieval (US-13)

**Feature Branch**: `009-us13-scope`

**Created**: 2026-07-12

**Status**: Draft

**Input**: User description: "US-13 — Zakres pytania (scope czatu). Silnik retrieval ze scope (pre-filtering metadanych przed wyszukiwaniem wektorowym): wszystkie dokumenty / folder z poddrzewem / pojedynczy plik. UI selektora i trwałość scope na rozmowie są forward-looking do US-14."

## Boundary note (US-13 vs US-14)

US-13 delivers the **retrieval engine with scope** — the piece that, given a scope and a question, returns the most relevant passages **within that scope** — plus the `ChatScope` value and its validation. The **scope selector UI**, persisting the scope on a conversation, and the answer generation itself belong to **US-14** and are out of scope here. The AC below are therefore expressed against the retrieval engine, testable now on real data.

## Clarifications

### Session 2026-07-12

- Q: How does scoped retrieval obtain the query embedding (shapes the seam interface and the empty-scope short-circuit)? → A: **Retrieval owns it.** The operation takes the scope + the **question text**. It first runs a **cheap metadata check** whether the scope has any ready documents; if empty, it returns the empty outcome **without embedding and without a vector search** (AC-5 saves both). Only when the scope is non-empty does it embed the question via the centralised embedding provider seam (US-06) and search. Tests use the deterministic embedding fake from US-06.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Folder scope covers the whole subtree (Priority: P1)

Asking within a folder's scope searches passages from documents in that folder **and every nested subfolder**, so a question scoped to "Umowy" also draws on "Umowy/2026".

**Why this priority**: This is the defining behavior of scoping and the one most likely to be implemented wrong (a naive folder filter that misses nested documents). It is the story that proves the pre-filtering-before-vector-search architecture.

**Independent Test**: Seed a folder with a nested subfolder, each holding a ready, indexed document; retrieve within the parent scope with a known query embedding; the results include passages from both documents. (Mandatory subtree prefix-match integration test.)

**Acceptance Scenarios**:

1. **Given** a folder "Umowy" with a subfolder "Umowy/2026", each containing a ready indexed document, **When** retrieval runs scoped to "Umowy", **Then** the eligible passages include chunks from documents in **both** "Umowy" and "Umowy/2026".
2. **Given** a sibling folder "Faktury" with its own ready document, **When** retrieval runs scoped to "Umowy", **Then** no passage from "Faktury" is returned.

---

### User Story 2 - Document scope limits to a single file (Priority: P1)

Asking within a single document's scope returns passages only from that document; results (and any future citations) point only to it.

**Why this priority**: The narrowest scope and the strongest isolation guarantee — a document scope must never leak passages from any other document.

**Independent Test**: With several ready documents indexed, retrieve scoped to one document; every returned passage belongs to that document and no other.

**Acceptance Scenarios**:

1. **Given** three ready indexed documents, **When** retrieval runs scoped to document A, **Then** every returned passage belongs to document A.
2. **Given** the same setup, **When** retrieval runs scoped to document A, **Then** no passage from document B or C is returned.

---

### User Story 3 - "All documents" scope, ready-only (Priority: P1)

The default scope searches across every ready document in the session, and never across documents still processing or failed, nor across another session's data.

**Why this priority**: The most common scope and the correctness floor: retrieval must respect status (ready-only) and session isolation on every path.

**Independent Test**: Seed ready, processing, and failed documents (and a second session's ready document); retrieve in the "all" scope; only the current session's ready documents contribute passages.

**Acceptance Scenarios**:

1. **Given** a session with two ready documents plus one processing and one failed, **When** retrieval runs in the "all" scope, **Then** only passages from the two ready documents are eligible.
2. **Given** another session also has ready documents, **When** retrieval runs in the "all" scope for the first session, **Then** no passage from the other session is returned (isolation).
3. **Given** more eligible passages than the configured result limit, **When** retrieval runs, **Then** at most the configured number of passages is returned, ordered by relevance (closest first).

---

### User Story 4 - Empty scope short-circuits (Priority: P1)

A scope that contains no ready documents returns an **empty result immediately**, without running a vector search or (in US-14) an answer-generation call — the caller renders "no documents in the selected scope".

**Why this priority**: Prevents pointless work and a misleading answer when there is nothing to ground on; the signal the future chat uses to show the empty-state message instead of calling the model.

**Independent Test**: Retrieve within a folder that has no ready documents; the result is an explicit "empty scope" outcome and no vector search is performed.

**Acceptance Scenarios**:

1. **Given** a folder with no ready documents (empty, or only processing/failed), **When** retrieval runs scoped to it, **Then** the result is an explicit empty outcome with zero passages.
2. **Given** an empty scope, **When** retrieval runs, **Then** no vector similarity search (and, forward-looking, no generation call) is performed.

---

### User Story 5 - Scope validation & isolation (Priority: P2)

A scope that names a folder or document not visible to the session is rejected as **scope-not-found**, so a stale or cross-session target never silently widens the search.

**Why this priority**: Guards correctness and isolation at the boundary; the future chat turns this into a "switch to All documents" prompt.

**Independent Test**: Retrieve with a folder/document id from another session (or a deleted one); the call fails with a stable scope-not-found outcome, and nothing is searched.

**Acceptance Scenarios**:

1. **Given** a folder id owned by another session, **When** retrieval is scoped to it, **Then** the call fails with `chat.scope_not_found` and no passages are returned.
2. **Given** a document id that has been deleted, **When** retrieval is scoped to it, **Then** the call fails with `chat.scope_not_found`.
3. **Given** an "all" scope, **When** retrieval runs, **Then** no scope target validation is needed (it always resolves to the session's ready documents).

---

### Edge Cases

- **Scope target removed mid-conversation** → the next retrieval returns `chat.scope_not_found` (US-14 will offer to switch to "All").
- **Processing/failed documents in scope** → skipped; only ready documents contribute. A scope whose only documents are processing/failed is an **empty scope** (US4), not scope-not-found.
- **Document scope on a not-yet-ready document** → the target exists but has no ready chunks → empty result (US4), not an error.
- **Folder scope on a folder that exists but is empty** → empty result (US4).
- **Fewer eligible passages than the result limit** → all eligible passages are returned (no padding).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST expose a retrieval operation that, given a scope and a **question (as text)**, returns the most relevant passages **within that scope**, each carrying enough to cite it (its text, source document, and page/location) and its relevance ordering.
- **FR-001a**: Retrieval MUST turn the question into a query vector itself, via the centralised embedding provider seam (US-06, one model for the whole index), and MUST do so **only after** the scope is confirmed non-empty (so an empty scope costs no embedding).
- **FR-002**: The scope MUST be one of: **all** (the session's documents), **folder** (a folder by id), or **document** (a document by id).
- **FR-003**: A **folder** scope MUST include documents in that folder **and all nested subfolders** (the whole subtree), via prefix match on the folder hierarchy path.
- **FR-004**: A **document** scope MUST include only that document's passages and no other document's.
- **FR-005**: Retrieval MUST consider **only ready** documents; documents still processing or failed MUST be excluded on every scope.
- **FR-006**: Retrieval MUST be **isolated to the current session** — passages from another session's documents MUST never be returned, on any scope.
- **FR-007**: Retrieval MUST return at most a **configured** maximum number of passages (result limit), ordered by relevance (closest first); when fewer are eligible, it returns all of them. The limit MUST be configuration-driven (no magic number).
- **FR-008**: When the scope contains **no ready documents**, retrieval MUST return an explicit **empty outcome** with zero passages and MUST NOT embed the question, perform a vector similarity search, nor (forward-looking) an answer-generation call — the emptiness is decided by a cheap metadata check first.
- **FR-009**: When a **folder** or **document** scope names a target not visible to the current session (nonexistent, deleted, or another session's), retrieval MUST fail with the stable code `chat.scope_not_found` and return no passages.
- **FR-010**: Retrieval MUST be **deterministic with respect to the scope passed in** — the same scope, question, and data yield the same eligible passages (scope is an input to the operation; its persistence on a conversation is US-14).
- **FR-011**: For a **document** scope, every returned passage MUST reference that document (so future citations point only to it).

### Key Entities *(include if feature involves data)*

- **ChatScope**: the search boundary for a question — a type (`all` / `folder` / `document`) and, for folder/document, the target id. Validated against the session before use.
- **Retrieved passage (chunk match)**: a passage eligible as grounding — its text, its source document (id + file name), its page/location, and a relevance measure used for ordering. (Passages come from the US-06 index; this feature reads them, it does not create them.)
- **Result limit (TopK)**: the configured maximum number of passages a retrieval returns.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: For a folder scope over a parent with a nested subfolder, **100%** of ready documents in the subtree (parent + descendants) are eligible for retrieval, and **0** documents outside the subtree are.
- **SC-002**: For a document scope, **0** passages from any other document are ever returned.
- **SC-003**: Across every scope, **0** passages from another session or from a non-ready document are ever returned.
- **SC-004**: An empty scope returns zero passages **without** embedding the question or performing a vector similarity search (verifiable: neither is executed), so no work is spent and no answer is generated.
- **SC-005**: A folder/document scope naming a target not visible to the session yields `chat.scope_not_found` in **100%** of cases, with no passages returned.
- **SC-006**: A retrieval never returns more than the configured result limit, and returns results ordered closest-first.

## Assumptions

- **Query embedding source (decided — see Clarifications)**: retrieval owns turning the question into a query vector via the centralised embedding provider seam (US-06), and performs the empty-scope short-circuit **before** embedding, so an empty scope costs neither an embedding nor a search.
- **Module placement**: the retrieval slice seeds a **`Chat`** module (US-14 fills in the conversation + generation); the stable error code is `chat.scope_not_found`.
- Chunks, embeddings, and the vector index exist from **US-06**; documents carry status + folder id (US-04/US-07); folders use a materialized path (US-09). This feature reads that data via raw SQL with an explicit session filter (the EF global query filter does not apply to raw SQL — same pattern as US-06).
- One embedding model governs the whole index (US-06); the query is embedded with the same model.
- The **scope selector UI**, persisting scope on a conversation and on each message, and the answer generation are **US-14** — out of scope here.

## Out of Scope

- Scope selector UI and the active-scope chip (US-14).
- Persisting the scope on a conversation / message metadata (US-14).
- Answer generation / LLM call and citations rendering (US-14/US-16).
- Multi-scope (several folders at once) and exclusions ("everything except X").
- Re-ranking beyond vector distance, hybrid keyword search, or query expansion.
