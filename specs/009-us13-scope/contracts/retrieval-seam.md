# Contract — Scoped retrieval seam (US-13)

US-13 exposes **no HTTP endpoint**. Its contract is the internal seam US-14 will dispatch behind a chat command. This documents that seam's behavior precisely (it is what the integration tests assert).

## `IScopedRetriever` (Chat/Domain)

```
Task<Result<ScopedRetrievalResult>> RetrieveAsync(ChatScope scope, string question, CancellationToken ct)
```

### Inputs
- `scope` — `All` | `Folder(id)` | `Document(id)` (construction-guarded so Folder/Document carry an id, All does not).
- `question` — the natural-language question text (the retriever embeds it itself).

### Outcomes

| Condition | Result |
|---|---|
| Folder/Document target not visible to the session (missing, deleted, cross-session) | `Failure(chat.scope_not_found)` (→ 404 when surfaced) — **no embedding, no search** |
| Valid scope, but no Ready-indexed content in it | `Success(ScopedRetrievalResult.Empty)` — **no embedding, no search** |
| Valid, non-empty scope | `Success(From(matches))` — `matches` ordered closest-first, `≤ RagOptions.TopK` |

### Guarantees (contract-level, asserted by integration tests)
- **Session isolation**: only the current session's chunks are ever returned; a cross-session target is `scope_not_found`, never a widened search.
- **Ready-only**: passages come only from `status = Ready` documents.
- **Folder = subtree**: a Folder scope returns passages from the folder and every descendant folder (materialized-path prefix), and nothing outside it.
- **Document = one file**: a Document scope returns passages only from that document.
- **Bounded + ordered**: at most `TopK` passages, ascending cosine distance.
- **Empty short-circuits**: an empty scope performs neither an embedding call nor a vector search.

### Ordering / determinism
- Ordering is by ascending cosine distance (`embedding <=> queryVector`). With the deterministic `FakeEmbeddingProvider` (US-06), the same question + data yields the same vectors and therefore the same ordering — so tests are stable.

## Downstream (US-14, out of scope here)
US-14 dispatches this seam behind a chat command, maps `chat.scope_not_found` to a "switch to All documents" prompt, renders the `IsEmptyScope` outcome as "no documents in the selected scope" (no generation call), persists the scope on the conversation, and feeds `Matches` to the prompt + citations.
