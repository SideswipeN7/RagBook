# Phase 1 — Data Model: US-13 scoped retrieval

**No persisted (database) entities and no migration.** US-13 reads the existing US-06/04/09 tables. This documents the value objects, the seam result shapes, config, and the error.

## Value objects (Chat/Domain)

### ChatScopeType (enum)

Stored/compared as int in the raw SQL scope predicate.

| Value | Int | Meaning |
|---|---|---|
| `All` | 0 | Every Ready document in the session |
| `Folder` | 1 | A folder and its whole subtree |
| `Document` | 2 | A single document |

### ChatScope

| Field | Type | Notes |
|---|---|---|
| `Type` | ChatScopeType | which boundary |
| `TargetId` | Guid? | required for Folder/Document; must be `null` for All |

- Construction guards (Domain): `All` ⇒ `TargetId` is null; `Folder`/`Document` ⇒ `TargetId` has a value. Factory methods `ChatScope.All()`, `ChatScope.Folder(id)`, `ChatScope.Document(id)`; an invalid combination is a construction error (Domain unit test), distinct from `scope_not_found` (a session-visibility failure decided by the retriever).

### RetrievedChunk (one result row)

| Field | Type | Source column |
|---|---|---|
| `ChunkId` | Guid | `chunks.id` |
| `DocumentId` | Guid | `chunks.document_id` |
| `FileName` | string | `documents.file_name` |
| `Text` | string | `chunks.text` |
| `PageNumber` | int? | `chunks.page_number` (null for TXT/MD) |
| `Distance` | double | `embedding <=> query` (cosine distance; smaller = closer) |

Carries everything a future citation (US-16) needs: the source document + page + the passage text.

### ScopedRetrievalResult

| Field | Type | Notes |
|---|---|---|
| `IsEmptyScope` | bool | true when the scope has no Ready-indexed content (AC-5) |
| `Matches` | IReadOnlyList<RetrievedChunk> | ordered closest-first; empty when `IsEmptyScope` or genuinely no hits |

Factory: `ScopedRetrievalResult.Empty` (IsEmptyScope=true, no matches) and `ScopedRetrievalResult.From(matches)`.

## Seam (Chat/Domain)

- `IScopedRetriever.RetrieveAsync(ChatScope scope, string question, CancellationToken) → Task<Result<ScopedRetrievalResult>>`
  - `Result.Failure(ChatErrors.ScopeNotFound)` when a Folder/Document target is not visible to the session.
  - `Result.Success(ScopedRetrievalResult.Empty)` when the (valid) scope has no Ready content — **no embedding, no search**.
  - `Result.Success(ScopedRetrievalResult.From(matches))` otherwise, `matches` ≤ `RagOptions.TopK`, ordered by ascending distance.

## Configuration — RagOptions

`SectionName = "Rag"` (bound like `QuotaOptions`).

| Property | Type | Default | Meaning |
|---|---|---|---|
| `TopK` | int | 8 | Maximum passages a retrieval returns (the SQL `LIMIT`) |

(Similarity threshold + grounding sentinel are added by US-14/US-17; not in US-13.)

## Error catalog (ChatErrors)

| Code | ErrorType | HTTP | Trigger |
|---|---|---|---|
| `chat.scope_not_found` | NotFound | 404 | Folder/Document scope target not visible to the session (nonexistent, deleted, or another session's) |

(An empty scope is **not** an error — it is a successful `IsEmptyScope` result.)

## Read dependencies (existing schema, unchanged)

- `chunks(id, document_id, user_session_id, index, text, page_number, embedding vector(1024))` — HNSW `vector_cosine_ops` index (US-06). Embedding EF-`Ignore`d; queried via raw SQL `<=>`.
- `documents(id, status int [Ready=1], folder_id uuid null, file_name, user_session_id, …)` (US-04/07).
- `folders(id, path text [/{id}/…/], user_session_id, …)` — `text_pattern_ops` prefix index (US-09).

## Invariants

- Every retrieval query filters `user_session_id = @session` explicitly (raw SQL bypasses the EF global filter).
- Only `status = Ready (1)` documents contribute passages.
- A Folder scope includes the folder itself and all descendants (prefix match); a Document scope includes exactly one document.
- `Matches.Count ≤ RagOptions.TopK`, ordered by ascending cosine distance.
- Empty scope ⇒ no embedding call and no vector search.
