# Phase 1 Data Model — US-17

No database entities, columns, or migrations. US-17 adds **wire + view state**, not persisted data (message
history is US-18).

## Message state (frontend `ChatExchange.status`)

| State | Meaning | Origin | Render |
|---|---|---|---|
| `streaming` | tokens arriving | during stream | live answer text |
| `complete` | a produced (Normal) answer, possibly partial, with `[n]` citations | `done.state = answered` | US-16 answer + used/searched sources |
| `no_answer` | **NoAnswerFound** — grounded refusal (deterministic OR prompt) | `done.state = no_answer` | neutral message + hints; „przeszukane fragmenty" only if `sources` present |
| `interrupted` | client stopped mid-stream | abort (US-15) | partial answer, „Przerwano." |
| `error` | technical failure | `error` event / non-2xx | error text + Try-again (US-19) |

`no_answer` is a **completed** message that is deliberately not an answer — visually informational, never the
error treatment, never Try-again.

## `done` event payload (additive change)

```jsonc
// before (US-14/15)
{ "groundsFound": true }

// after (US-17) — additive `state`; `groundsFound` retained
{ "groundsFound": true,  "state": "answered"  }   // Normal answer
{ "groundsFound": true,  "state": "no_answer" }   // prompt-refusal (passages were in context → sources event sent)
{ "groundsFound": false, "state": "no_answer" }   // deterministic cut-off (no model call, no sources event)
```

`state ∈ { "answered", "no_answer" }`. Event **names and order are unchanged**: `sources?` → `token*` → `done`
(or `error`). The frontend keys the message state off `state`; it decides whether to show „przeszukane fragmenty"
by whether a `sources` event was received (i.e. `exchange.sources.length > 0`).

## Refusal rule (domain)

`GroundingPrompt.IsRefusal(answer)` ≡ `answer.Trim().Equals(RefusalPhrase, StringComparison.Ordinal)`.

| Accumulated answer | `IsRefusal` | `done.state` |
|---|---|---|
| `"Nie znalazłem odpowiedzi w wybranych dokumentach."` | true | `no_answer` |
| `"  Nie znalazłem odpowiedzi w wybranych dokumentach.  "` (whitespace) | true | `no_answer` |
| `"Umowa nie zawiera kary umownej [1]. Nie znalazłem odpowiedzi…"` (mid-text) | false | `answered` |
| `"Okres wynosi 3 miesiące [1]."` (normal) | false | `answered` |
| `"Zawiera A [1]; brak informacji o B."` (partial) | false | `answered` |

## No-answer view inputs (frontend)

| Input | Source |
|---|---|
| neutral heading text | fixed: "Nie znalazłem tego w dokumentach" |
| next-step hints | fixed list: broaden scope · check document is Ready · rephrase |
| searched fragments | `exchange.sources` (US-16 `Source[]`), rendered as collapsed „przeszukane fragmenty" **only when non-empty**, with the existing preview |
