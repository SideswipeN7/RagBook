# Contract — No-basis / refusal (US-17)

Additive extension of the US-14/15 streaming ask contract. **No** event is renamed or reordered.

## Endpoint

`POST /api/chat/ask` — unchanged request (`{ question, scope }`), unchanged `text/event-stream` response with the
event sequence `sources?` → `token*` → `done` (or a terminal `error`).

## Changed event: `done`

```
event: done
data: { "groundsFound": <bool>, "state": "answered" | "no_answer" }
```

- `state` is **new**; `groundsFound` is retained unchanged.
- `state = "answered"` ⇔ a produced (Normal) answer was streamed (may be partial, may cite `[n]`).
- `state = "no_answer"` ⇔ NoAnswerFound, from either:
  - **deterministic** cut-off — emitted by `StreamInsufficientAsync` **without** a model call, with **no**
    preceding `sources`/`token` events → `{ "groundsFound": false, "state": "no_answer" }`;
  - **prompt-refusal** — the fully-accumulated answer satisfies `GroundingPrompt.IsRefusal` → `{ "groundsFound":
    true, "state": "no_answer" }`, preceded by the normal `sources` event and the (sentinel) `token`s.

## Detection rule

```csharp
// GroundingPrompt (Domain)
public static bool IsRefusal(string answer) =>
    answer.Trim().Equals(RefusalPhrase, StringComparison.Ordinal);
```

The endpoint accumulates streamed deltas and, on normal completion, sets `state = IsRefusal(accumulated) ?
"no_answer" : "answered"`. A mid-stream provider failure remains an `error` event (US-14/19) — never `no_answer`.

## Invariants

1. Event names and order are unchanged; only the `done` payload gains a field.
2. The deterministic path performs **no** model call and emits **no** `sources` event.
3. A refusal never carries an error code and never triggers Try-again; a technical error never carries `state:
   "no_answer"`.
4. An answer that merely contains the sentinel mid-text, or a partial answer, yields `state: "answered"`.

## Frontend consumption

- `done` handler parses `{ groundsFound, state }`; sets `ChatExchange.status = state === "no_answer" ? "no_answer"
  : "complete"` (retains `groundsFound`).
- `status === "no_answer"` renders the neutral NoAnswerFound view (message + hints), showing the collapsible
  „przeszukane fragmenty" **iff** `sources.length > 0`.
