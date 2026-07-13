# Phase 1 — Data Model: US-16 citations

**No persisted entities, no migration.** Additive DTO fields + client view state.

## Backend — extended shapes (Chat)

### GroundingPassage (US-14) — **+ ChunkId**

| Field | Type | Notes |
|---|---|---|
| `Number` | int | 1-based `[n]` |
| `DocumentId` | Guid | source document |
| `FileName` | string | |
| `PageNumber` | int? | null for TXT/MD |
| `Text` | string | full passage text |
| **`ChunkId`** | Guid | **NEW** — the chunk's id (for the citation contract) |

`PromptBuilder` fills `ChunkId` from `RetrievedChunk.ChunkId` (US-13).

### SourceDto (US-14 `sources` event) — **+ Text, ChunkId**

| Field | Type | Notes |
|---|---|---|
| `Number` | int | the `[n]` |
| `DocumentId` | Guid | |
| `FileName` | string | |
| `PageNumber` | int? | |
| **`Text`** | string | **NEW** — full chunk text (for the preview) |
| **`ChunkId`** | Guid | **NEW** — chunk id (deterministic mapping) |

(No server `snippet` field — the client truncates `Text`.)

## Frontend — view state (Angular)

### Source (US-15 `ChatStore.Source`) — **+ text, chunkId**

`{ number, documentId, fileName, pageNumber, text, chunkId }` — the list snippet is `text` truncated (~200 chars).

### AnswerSegment (from `citation-parser`)

- `{ type: 'text', value: string }` — a run of answer text (including out-of-range `[n]` left as text).
- `{ type: 'citation', number: number }` — an in-range `[n]` marker, clickable.

### Preview state

Per `chat-answer`: `openSource: Source | null` — the passage shown in the in-app preview panel.

## Consumed SSE (US-14, unchanged except added fields)

`sources` event data: `[{ number, documentId, fileName, pageNumber, text, chunkId }]` (was without `text`/`chunkId`).

## Invariants

- `[n]` (in range) resolves to exactly the source numbered `n` (same `chunkId`) — deterministic.
- An out-of-range `[n]` renders as plain text (no link) and logs a quality warning; rendering never breaks.
- A passage is "used" iff its number appears in the completed answer; used are shown, the rest collapsed.
- The preview reads the exchange's stored `text` — works after the source document is deleted (in-session).
- A no-basis answer shows no used-source list.
