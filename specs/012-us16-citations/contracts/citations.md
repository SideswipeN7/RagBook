# Contract — citations (US-16)

US-16 adds **no new endpoint** — it extends US-14's `sources` event and US-15's rendering.

## `sources` event payload (extended)

```
event: sources
data: [
  { "number": 1, "documentId": "…", "fileName": "umowa.pdf", "pageNumber": 3,
    "text": "…full chunk text…", "chunkId": "…" },
  …
]
```
Added fields (additive, no rename/reorder): **`text`** (full chunk, for the preview) and **`chunkId`** (deterministic mapping). The existing `number`/`documentId`/`fileName`/`pageNumber` are unchanged.

## Client rendering contract

- **Segments:** the completed answer is parsed against the exchange's source numbers (`1..K`):
  - in-range `[n]` → a **clickable citation** opening passage `n`'s preview;
  - out-of-range `[n]` → **plain text** (no link) + a `console.warn`;
  - text between/around markers is preserved (markers never break the text).
- **Source list:** **used** passages (number present in the answer) shown with file, page, and a client-truncated snippet; the rest under a collapsible **"pozostałe przeszukane fragmenty"** (closed by default). No markers used but passages present → all shown with a "none explicitly cited" note + a warning.
- **Preview:** an **in-app panel** (no native dialog) with the passage's full `text` + file + page; dismissible. Reads the exchange's stored data, so it works after the document is deleted (in-session).
- **No-basis:** a `groundsFound:false` answer shows **no** used-source list.

## Guarantees (asserted by tests)

- Each in-range `[n]` opens exactly its passage's preview (by `chunkId`).
- An out-of-range `[n]` never links and never breaks rendering; a warning is logged.
- Used vs searched split is correct; the preview shows full text after the document is deleted (in-session).
- The backend `sources` event carries `text` + `chunkId`.
