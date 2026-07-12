# Phase 0 — Research & Decisions: US-13 scoped retrieval

Grounded in the now-present US-06 code (chunks + `IEmbeddingProvider` + raw-SQL vector), US-09 (folder materialized path), US-04/07 (document status + folder_id). All decisions respect the settled constraints (pre-filter metadata before vector search; one embedding model; config-driven TopK; explicit session filter in raw SQL).

## D1 — The retrieval query (pre-filter then vector search)

- **Decision**: One raw SQL statement, mirroring `ChunkRepository`'s `dbContext.Database` convention on the read side:
  ```sql
  SELECT c.id, c.text, c.page_number, c.document_id, d.file_name,
         c.embedding <=> CAST(@queryVec AS vector) AS distance
  FROM chunks c
  JOIN documents d ON d.id = c.document_id
  LEFT JOIN folders f ON f.id = d.folder_id
  WHERE d.user_session_id = @session
    AND d.status = 1                          -- Ready (enum stored as int; NOT the string 'Ready')
    AND (
          @scopeType = 0                                          -- All
       OR (@scopeType = 2 AND d.id = @documentId)                 -- Document
       OR (@scopeType = 1 AND f.path LIKE @scopePath || '%')      -- Folder subtree
        )
  ORDER BY c.embedding <=> CAST(@queryVec AS vector)
  LIMIT @topK;
  ```
- **Rationale**: Single index-assisted scan. The HNSW `vector_cosine_ops` index (US-06, `ix_chunks_embedding_hnsw`) serves the `<=>` **cosine distance** ordering; the `text_pattern_ops` index on `folders.path` (US-09) serves the `LIKE prefix || '%'`. Pre-filtering by session/status/scope in the same `WHERE` is the settled "hybrid filtering" decision (one chunk table, filter in the query — no per-folder indexes).
- **Status is int, not string** (⚠️): `DocumentStatus.Ready = 1` is stored as `integer`. The binding SQL in the US doc shows `d.status = 'Ready'`; the real query MUST use `d.status = 1`.
- **Query vector literal**: reuse `ChunkRepository`'s approach — format the `float[]` as `[v1,v2,…]` with `ToString("R", InvariantCulture)` and `CAST(... AS vector)`. (The embedding column is EF-`Ignore`d on EF Core 10, so parameters go through as a cast literal, not an EF-mapped `Vector`.)
- **Reading rows**: use `dbContext.Database.GetDbConnection()` + an `NpgsqlCommand`/reader (full control over the vector literal + column reads) — the embedding column is not EF-mapped so `FromSql` onto the `Chunk` entity is unavailable; a keyless projection is read manually into `RetrievedChunk`.
- **Alternatives rejected**: EF LINQ (can't express `<=>` on an ignored column); per-folder indexes (rejected architecture); recursive CTE for subtree (the materialized-path prefix match is O(1)-indexed and already how US-09 models subtrees).

## D2 — Scope resolution & validation

- **Decision**:
  - **All** → no target; predicate reduces to session + Ready.
  - **Folder(id)** → resolve via the existing `IFolderRepository.GetByIdAsync(id)` (session-scoped; `null` → `chat.scope_not_found`); use the folder's `Path` (format `/{id}/…/`, leading+trailing slash, N-format guids) as `@scopePath`. A document directly in the folder (folder path == scopePath) matches `LIKE scopePath || '%'` (`%` matches empty); descendants match by prefix.
  - **Document(id)** → validate existence in session with a cheap `SELECT 1 FROM documents WHERE id=@id AND user_session_id=@session` (any status — a not-yet-Ready target is *valid but empty*, not not-found); `null` → `chat.scope_not_found`.
- **Rationale**: Reuses US-09's session-scoped folder read and path format; keeps a cross-session/deleted target from silently widening the search (isolation, FR-009). Distinguishes **not-found** (target invisible) from **empty** (target visible, no Ready chunks) per the spec edge cases.
- **Alternatives rejected**: trusting the caller's scope id (breaks isolation); treating a processing-document scope as not-found (it's a valid empty scope).

## D3 — Empty-scope short-circuit (before embedding)

- **Decision**: After scope validation, run a cheap **existence** check:
  ```sql
  SELECT EXISTS(
    SELECT 1 FROM chunks c JOIN documents d ON d.id = c.document_id
    LEFT JOIN folders f ON f.id = d.folder_id
    WHERE d.user_session_id = @session AND d.status = 1 AND (<scope predicate>) );
  ```
  If `false` → return `ScopedRetrievalResult.Empty` **without** calling `IEmbeddingProvider` and **without** the vector search.
- **Rationale**: Realizes AC-5 as clarified (retrieval owns the embedding, so it can skip it). Chunks exist only for Ready documents (the worker writes them on `MarkReady`), so "has chunks in scope" == "has ready indexed content in scope". Saves an embedding round-trip and a search on empty scopes.
- **Alternatives rejected**: embedding first then finding zero matches (wastes the embedding); counting documents instead of chunks (a Ready document with zero chunks — pathological — would falsely look non-empty).

## D4 — Query embedding (reuse US-06 seam)

- **Decision**: Embed the question with `IEmbeddingProvider.EmbedBatchAsync([question])` → first vector. The provider is the US-06 centralised seam: deterministic `FakeEmbeddingProvider` when no `Embedding:ApiKey` (dev/tests), Voyage otherwise — one model for the whole index, so query and chunk vectors are comparable.
- **Rationale**: The clarify decision (retrieval owns embedding). No new provider; tests get determinism for free (same text → same vector), so a seeded chunk and a query over the same text are exactly comparable.
- **Testing the short-circuit**: wrap the provider in a **counting** test double to assert `EmbedBatchAsync` is **not** called for an empty scope, and **is** called once otherwise.

## D5 — TopK config (`RagOptions`)

- **Decision**: New `RagOptions` (`SectionName = "Rag"`, `TopK` default **8**), bound in `Program.cs` like `QuotaOptions`; `"Rag": { "TopK": 8 }` in `appsettings.json`. The retriever reads `IOptions<RagOptions>.Value.TopK` for the `LIMIT`.
- **Rationale**: Constitution: RAG params live in config, no magic numbers. `RagOptions` is the home for TopK now and the similarity threshold + sentinel later (US-14/US-17) — this feature adds only `TopK`.
- **Alternatives rejected**: a literal LIMIT (magic number); reusing an unrelated options class.

## D6 — Module & error naming

- **Decision**: New **`Chat`** module (`Modules/Chat/`) seeded by US-13's retrieval engine; US-14 fills in the conversation + generation + selector. Closed catalog `ChatErrors` with `chat.scope_not_found` (`ErrorType.NotFound` → 404, consistent with session-isolation 404s).
- **Rationale**: Scope is a chat concept; establishing the `Chat` module now gives US-14 its home. The `chat.*` prefix matches the settled naming in the US input.
- **Alternatives rejected**: a `Rag` module (retrieval is an internal capability of chat, not a user-facing epic of its own); putting retrieval in `Documents` (it composes documents+folders+chunks and is consumed by chat — a cross-cutting read, not a Documents feature).

## D7 — No endpoint / no Wolverine dispatch in US-13

- **Decision**: US-13 ships the `IScopedRetriever` **seam** only; no HTTP endpoint and no Wolverine query. Integration tests exercise the impl by resolving it from the host's DI (or constructing it against the Testcontainers `RagBookDbContext`), like US-08 tested its repository directly.
- **Rationale**: There is no chat surface yet (US-14). Adding an endpoint now would be speculative. The seam + Result contract is exactly what US-14 dispatches behind its chat command.

## Open items deferred (not blocking)

- Similarity **threshold** / grounding sentinel → US-17 (`RagOptions` will grow).
- The chat **command/endpoint**, conversation persistence, and the **scope selector UI** → US-14.
- Re-ranking, hybrid keyword search, query expansion → out of scope.
