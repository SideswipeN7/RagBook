# Project agent memory — RagBook

Project-intrinsic knowledge that should travel with the code. The **constitution**
(`.specify/memory/constitution.md`) is the binding source; this file elaborates, never contradicts it.

## What this is

RagBook — a case-study RAG assistant (upload → pgvector index → streamed answers with citations).
**.NET 10** modular monolith (vertical slices, `Result<T>`, Wolverine dispatch) + **Angular** SPA,
orchestrated by **.NET Aspire**, backed by **PostgreSQL + pgvector**, targeting **GCP Cloud Run**.
Currently implemented: **US-01 (user session / data isolation)** and **US-05 (file quota)** + the
greenfield foundation. The remaining stories live in `docs/features/US-*.md`; spec-driven artifacts in
`.specify/` and `specs/001-us01-session/` + `specs/002-us05-quota/`.

## Architecture / conventions

- **Fixed project shape** (do not add top-level projects without a constitution change): `RagBook`
  (Core), `RagBook.API`, `RagBook.Infrastructure`, `RagBook.Infrastructure.Migrations`,
  `RagBook.AppHost`, `RagBook.ServiceDefaults`, `src/Web` (Angular), `tests/*`.
- **Vertical slices**: `src/RagBook/Modules/<Module>/{Domain,Errors,Features}`. One folder per feature
  (`CreateResource`, `GetResource`, …); handler named feature+role (`CreateResourceCommandHandler`).
  Cross-module calls go through events, never direct references.
- **CQRS + Result**: `ICommand`/`IQuery` markers (`Shared/Messaging`); handlers return `Result<T>` and
  **never throw for expected failures**. Each module owns a closed error catalog
  (`Errors/<Module>Errors.cs`, stable `module.code`) + a `<Module>ExceptionHandler` (infra→code via
  `IPersistenceExceptionClassifier`). A global `IExceptionHandler` writes RFC 9457 ProblemDetails with
  a `code` — no naked 500s.
- **Data isolation** (US-01): entities implement `ISessionOwned`; `RagBookDbContext` applies a global
  query filter keyed to the injected `ISessionContext`; another session's resource → **404, not 403**.
  `SessionStampingInterceptor` stamps `UserSessionId` on insert; `AuditingInterceptor` stamps
  `IAuditable` via **`TimeProvider`** (never `DateTime.UtcNow`). `SessionResource` is the reference
  session-owned slice future modules copy (kept permanently — captain decision).
- **Config-driven, no magic numbers**: cookie/session tunables in `SessionCookieOptions` (`Session:*`).
- **C# style**: primary constructors; always braces; blank line before every `return`; `var` when the
  type is obvious; sorted usings; `ValueTask` when fully async; flow `CancellationToken`; XML docs on
  public members. Solution builds with **`TreatWarningsAsErrors=true`** (Migrations project excepted —
  generated code). NuGet versions are centralized in `Directory.Packages.props`; TFM `net10.0` via
  `Directory.Build.props`.
- **Frontend**: Angular standalone, OnPush, **signals**, new control flow (`@if`/`@for`). Design tokens
  from `DESIGN.md` in `src/Web/src/styles/tokens.scss` — **never inline hex**. The 404 interceptor
  (`core/not-found.interceptor.ts`) maps 404 → "resource does not exist"; the SPA holds no isolation
  logic (backend-managed cookie).

## Commands

```sh
# Backend
dotnet build RagBook.slnx
dotnet test  tests/RagBook.Domain.Tests            # no Docker
dotnet test  tests/RagBook.Application.Tests        # no Docker
dotnet test  tests/RagBook.Api.IntegrationTests     # Testcontainers PostgreSQL — START DOCKER FIRST
dotnet test  <proj> --filter "FullyQualifiedName~<Name>"   # single test

# EF migrations (created here; applied out-of-band, NEVER at startup)
dotnet tool restore                    # restore the pinned dotnet-ef local tool (dotnet-tools.json)
dotnet ef migrations add <Name> \
  --project src/RagBook.Infrastructure.Migrations \
  --startup-project src/RagBook.Infrastructure.Migrations \
  --context RagBookDbContext           # uses RagBookDbContextFactory (design-time)

# Frontend
cd src/Web && npm install && npm test   # ng test (Karma/Jasmine, headless)
```

## Uruchom lokalnie (run locally)

```sh
cd src/Web && npm install && cd -          # install SPA deps once (prerequisite for the web resource)
dotnet run --project src/RagBook.AppHost   # Aspire: PostgreSQL (pgvector) + API + Angular dev server — Docker required
```

The Aspire dashboard prints its URL on startup. The API reads connection string `ragbookdb` (injected
by Aspire); running the API standalone requires that connection string in configuration.

## Sharp edges

- **`SessionOptions` name clash**: our cookie options are `SessionCookieOptions` — the plain
  `SessionOptions` collides with `Microsoft.AspNetCore.Builder.SessionOptions` (globally imported).
- **FluentAssertions pinned to 7.2.2** (last Apache-2.0 release); v8+ carries a commercial license that
  trips the constitution's "no license-warning dependencies" rule.
- **EF 10 API**: use `GetDeclaredQueryFilters()` (not the obsolete `GetQueryFilter()`); Testcontainers 4
  requires `new PostgreSqlBuilder("image:tag")` (parameterless ctor is obsolete).
- **Integration tests need Docker**: without a running engine the Testcontainers tier errors at
  container startup, not on assertions. The middleware and offline query-filter tests run without it.
- **Angular in AppHost via `AddExecutable`**: Aspire 13.4.6 has no compatible `AddNpmApp`
  (`Aspire.Hosting.NodeJs` and the CommunityToolkit Node hosting are stuck on the incompatible 9.x
  line), so `RagBook.AppHost` orchestrates the SPA with core `AddExecutable("web","npm","../Web",
  "run","start")`. `npm install` in `src/Web` is a prerequisite before `dotnet run`-ing the AppHost.
- **Quota atomicity (US-05, AC-5)**: quota check-and-insert must be **atomic** or two concurrent
  uploads at 9/10 both admit. The `Documents` module does it with a **transaction-scoped advisory lock**
  — `DocumentQuotaRepository.TryAddWithinQuotaAsync` runs `SELECT pg_advisory_xact_lock(<key>)` (key
  derived from the session GUID), **re-reads usage under the lock**, then inserts inside one EF
  transaction. Do not "optimize" the re-read away — it is what makes the check atomic. Limits are
  config-driven via `QuotaOptions` (`Quota:*` section); MB are decimal (1 MB = 1,000,000 bytes).
  `DocumentOrigin.User` (incl. `Failed`) counts toward quota, `DocumentOrigin.Demo` does not.
- **Folder hierarchy (US-09, `Folders` module)**: materialized path with **ids as segments**
  (`/A/B/C/`, self-inclusive, leading+trailing slash — see `FolderPath`). Depth = segment count, so the
  3-level cap is a check on the parent's path. **Rename changes only `Name`** — never the path or
  descendants (that is why ids, not names, are the segments). Per-parent name uniqueness is
  **case-insensitive** and enforced by **two partial unique indexes** on `(user_session_id, [parent_id,]
  LOWER(name))` — a separate one `WHERE parent_id IS NULL` for roots, because Postgres treats NULL
  parent_ids as distinct (a single composite constraint would miss root duplicates). These functional +
  partial indexes are raw SQL in the `AddFolders` migration (EF's fluent API can't model them). Names
  are trimmed before validation/uniqueness. Delete is **empty-only**: the self-`parent_id` FK is
  `ON DELETE RESTRICT` (DB refuses to drop a folder with children → mapped to `folder.not_empty`), and
  the "has files" arm is the `IFolderFileProbe` seam whose `NoFolderFilesProbe` no-op is replaced by
  **US-04** once `documents.folder_id` exists. Limits are config-driven via `FolderOptions`
  (`Folders:*`). `FolderTreeStore` (signals) + `app-folder-tree` back the UI.
- **Document upload (US-04, `Documents/Features/UploadDocument`)**: validate **by content, not
  extension** — `FileTypeDetector` checks the `%PDF-` signature, else requires valid UTF-8 text and
  classifies `.md`→markdown / else plain (`document.unsupported_file_type`; 0 bytes →
  `document.empty_file`). Order: empty → type → size → folder → **store** → **atomic quota admit**. Size/
  count/total limits are the **US-05 `QuotaOptions`** (no new limits). `IFileStorage`/`LocalFileStorage`
  keep blobs **outside Postgres** under config `FileStorage:RootPath`; **store-then-record with
  compensation** — the handler deletes the blob if the admit/insert fails (no orphans). Duplicate names
  auto-suffix `name (n).ext` from **1**, computed **under the session advisory lock** (which serializes a
  session's uploads) — NOT via same-transaction 23505 retry (that aborts the tx on Postgres); two partial
  unique indexes on `(folder_id, LOWER(file_name))` are a backstop. `NoFolderFilesProbe` was **replaced**
  by `DocumentFolderFileProbe` (US-09 AC-5 now live). Core publishes `DocumentUploaded` via the
  `IEventPublisher` abstraction (impl `WolverineEventPublisher` in the API host — Core never references
  Wolverine); the Documents module reads folder existence through its own `IFolderReference` seam (no
  Core→Folders reference).
- **Folder+document tree (US-07, `Tree` module)**: `GET /api/tree` returns folders + documents in **one**
  response via the single **`ITreeReader`** seam (impl `TreeReader` in Infrastructure runs **two**
  session-scoped `AsNoTracking` queries — folders `LOWER(name)`, documents `Origin != Demo` newest-first;
  no N+1). The Tree slice references **neither** the Folders nor the Documents module (its own DTOs
  `TreeFolder`/`TreeDocument`, §I). Added a nullable **`documents.failure_reason`** column here
  (forward-looking — **US-06 fills it**; US-07 only displays, generic fallback when null). **Frontend:** a
  unified **`@angular/cdk` `cdk-tree`** (`app-document-tree`) **replaced** the folders-only
  `app-folder-tree`; `TreeStore` (signals) composes the nested tree + owns expansion in `sessionStorage`;
  folder mutations reuse `FolderTreeStore` **and must call `TreeStore.refresh()`** (the tree reads from
  `/api/tree`, not `/api/folders`). Decimal size via `core/file-size.ts`. `DocumentUploadStore` now
  refreshes `TreeStore` (not `FolderTreeStore`) after an upload.
- **BYOK key (US-02, `Settings` module)**: the user's Anthropic key lives **only** in `IApiKeyStore` over
  `IMemoryCache` (first cache in the repo — `AddMemoryCache()`), keyed by session, TTL from
  `ApiKeyStoreOptions` via `TimeProvider` (the store keeps an explicit `ExpiresAt` **and** a cache
  relative-expiry backstop — the explicit one is the authoritative, testable TTL; do NOT set the cache's
  absolute expiry from a fake clock, it evicts immediately against wall-clock). **Never in the DB, never
  logged, mask-only in responses** (`ApiKeyMask` → `sk-ant-api03-…XXXX`); all `/api/settings/api-key`
  responses are `no-store`. Validation is a **non-generative** `GET /v1/models` behind `IApiKeyValidator`
  (first external-provider seam — resilient named `HttpClient` + `AddStandardResilienceHandler`; tests swap
  an in-memory fake, no test hits Anthropic) returning three-way `Valid|Rejected|Unavailable` →
  `active` / `settings.invalid_api_key` (400) / `settings.validation_unavailable` (503). Empty/malformed
  keys are rejected **in the handler** (same `invalid_api_key` code) — NOT via FluentValidation (which
  would emit a generic validation code the frontend can't map). Per-session throttle (`IApiKeyThrottle`,
  fixed window) is checked **before** the upstream call → `settings.too_many_attempts` (429). Generation
  guard = `IAnthropicClientFactory.CreateForSession()` → `settings.api_key_missing` (401) when no key
  (US-14 consumes it). Added two **additive** shared `ErrorType` values — `RateLimited`→429,
  `Unavailable`→503 (+ `ErrorStatusMapper`). Integration tests swap the validator via
  `ConfigureTestServices` in the factory's **own** `ConfigureWebHost` (NOT a derived `WithWebHostBuilder`
  host — that re-runs Wolverine codegen and fails handler construction). Frontend: `ApiKeyStore` (signals)
  + `app-api-key-settings` in the shell (no router yet); `chatLocked` computed gates the future chat UI.
- **Background indexing (US-06, `Documents/Processing`)**: durable Wolverine handler on `DocumentUploaded`
  → extract (`ITextExtractor`: PdfPig / plain) → `StructuralChunker` (config `Chunking:*`, page numbers)
  → embed in batches (`IEmbeddingProvider`) → `chunks`(pgvector) → `Document.MarkReady/MarkFailed`. The
  worker has **no HTTP session**: it reads the target session-agnostically (`IgnoreQueryFilters`) then
  `ISessionInitializer.Initialize(doc.session)` so chunks/updates stay session-scoped. **Embedding
  provider** = deterministic `FakeEmbeddingProvider` unless `Embedding:ApiKey` set → `VoyageEmbeddingProvider`
  (`voyage-3.5`/1024). **Retry is in the handler** (bounded, then terminal `Failed`), not Wolverine, so
  it's testable; `IChunkRepository.ReplaceForDocumentAsync(Document, chunks)` = delete+insert+status in one
  tx (idempotent, no-partial). **pgvector gotcha:** `Pgvector.EntityFrameworkCore` is incompatible with
  EF Core 10 → EF **Ignores** `Chunk.Embedding`; the `embedding vector(1024)` column is written via raw SQL
  (text→`vector` cast) and the migration `CREATE EXTENSION vector` + HNSW/unique via raw SQL; the model
  snapshot must NOT contain `embedding` (or `MigrateAsync` throws PendingModelChanges). **Wolverine
  durability** (`Wolverine:DurabilityEnabled`, default on) provisions its own tables via
  `AddResourceSetupOnStartup`; tests set it **false** and invoke `ProcessDocumentHandler.Handle` **directly**
  (seed docs WITHOUT publishing `DocumentUploaded`, else the in-memory bus double-processes). Status pushed
  over **SSE** `GET /api/documents/status/stream` → Angular `DocumentStatusStore` refreshes the tree.
  PdfPig pinned to `1.7.0-custom-5` (other NuGet versions have broken transitive deps).
- **Scoped retrieval (US-13, `Chat` module)**: `IScopedRetriever` (Chat/Domain) + `ScopedRetriever`
  (Infrastructure `SharedContext/Retrieval`) is the **engine only** — no endpoint/UI/migration (that's
  US-14). `ChatScope` = `All | Folder(id) | Document(id)` (factory-only, invalid combos unrepresentable).
  Order: **resolve/validate scope** (folder path / document existence via raw SQL scoped to the session;
  not visible → `chat.scope_not_found`) → **cheap `EXISTS`** (session + `status=1` + scope predicate); empty
  → `ScopedRetrievalResult.Empty` with **no embedding, no search** (AC-5) → embed the question via the US-06
  `IEmbeddingProvider` → raw-SQL cosine `<=>` search. **Gotchas:** `d.status = 1` (int `Ready`), NOT the
  string `'Ready'`; the embedding column is EF-`Ignore`d so the read uses `dbContext.Database.GetDbConnection()`
  + `DbCommand` (query vector as a **bound text param** `CAST(@queryVec AS vector)`, not interpolated); the
  session filter is **explicit** in the WHERE (raw SQL bypasses the global query filter, as in US-06); folder
  subtree = `f.path LIKE @scopePath || '%'` (US-09 materialized path). `Rag:TopK` (default 8) is the `LIMIT`
  (config-driven). Tests seed via the real `IChunkRepository` + the deterministic `FakeEmbeddingProvider`
  (same vectors → query/chunk comparable); the empty-scope test uses a `CountingEmbeddingProvider` to prove
  the question was not embedded. **Testcontainer factory dispose:** dispose `base` (host/connections) BEFORE
  the pgvector container, or teardown throws an AggregateException querying a gone DB.
- **Delete document (US-08, `Documents/Features/DeleteDocument`)**: `DELETE /api/documents/{id}` →
  `IDocumentDeletionRepository.DeleteAsync(id)` (Infrastructure): session-scoped tracked load (null →
  `false` → `document.not_found`/404), **transactional row delete → chunks cascade at the DB** (US-06 FK) →
  commit, **then best-effort `IFileStorage.DeleteAsync`** in try/catch + `ILogger` warning (orphan
  tolerated, FR-004). Handler is trivial (`bool → Result`). Cross-session / repeat delete → 404
  (idempotent). Delete-during-processing relies on the US-06 quiet abort. Frontend: `DocumentActionsStore.delete`
  (DELETE → refresh Tree+Quota) + a **separate** document-leaf inline confirm in `app-document-tree`
  (`confirmingDeleteDocumentId`, distinct from the folder confirm). **Testing best-effort blob tolerance**:
  do NOT use `WithWebHostBuilder`+`ConfigureTestServices` (Wolverine codegen fails to resolve the handler in
  the derived host) — construct `DocumentDeletionRepository` directly with a throwing `IFileStorage` stub +
  `NullLogger`.
- **RAG ask + streaming (US-14, `Chat` module)**: `POST /api/chat/ask` (question+scope in **body**, never
  URL). Flow = validate (`chat.invalid_question`) → **key guard at the endpoint** via `IAnthropicClientFactory`
  (`settings.api_key_missing`, before any provider call — endpoint composes Settings+Chat; Core `Chat` never
  refs Settings) → `IAskQuestionPipeline` (retrieve US-13 → threshold `distance ≤ 1 − SimilarityThreshold` →
  `PromptBuilder`) → `AskOutcome.Answerable | InsufficientGrounding`. **SSE is written manually from the
  endpoint** (`event: …\ndata: …\n\n` + flush, like US-06 status stream) — NOT a Wolverine command (a token
  stream isn't a single `Result`). **Peek the first delta before writing any bytes**: a generation failure
  there is a ProblemDetails (headers unsent); after that it's an SSE `error` event (AC-5). `IAnswerGenerator`
  (`AnthropicAnswerGenerator`) streams `/v1/messages` `stream:true`, parses `content_block_delta.text`; its
  named `HttpClient` has **`Timeout=InfiniteTimeSpan` and NO `AddStandardResilienceHandler`** — the standard
  total-timeout/retry would truncate/re-issue a live stream (C1). Tests swap `FakeStreamingAnswerGenerator`
  (scripts deltas + pre/post-first-delta failures); the real generator is tested with a canned
  `HttpMessageHandler`. Deterministic fake embeddings: a question repeating a chunk's text ⇒ distance ~0
  (answerable); a different question ⇒ ~orthogonal ⇒ below threshold (insufficient). `GroundingPrompt.RefusalPhrase`
  is the exact sentinel US-17 will detect. Codes: `chat.invalid_question`/`provider_rate_limited`/`provider_unavailable`
  + reused `settings.api_key_missing`/`invalid_api_key`. Stateless (persistence = US-18); no frontend (US-15).
- **Streaming chat UI (US-15, `src/Web/src/app/chat` + `core/chat.store.ts`)**: consumes US-14's SSE via a
  streaming **`fetch`** (NOT Angular `HttpClient`, NOT `EventSource` — the endpoint is a POST token stream) +
  a pure `sse-parser.ts` (unit-tested; ignores `:` heartbeat comments). `ChatStore` holds a **multi-turn
  in-memory thread** (signals); `token`→append, `sources`→list, `done`/`error`/abort→status. **The fetch MUST
  send `credentials: 'same-origin'`** (session cookie) else the ask hits a fresh session → 401. `stop()` =
  `AbortController.abort()`; a new ask aborts the previous (one active). Stream-without-`done` → error. Codes→PL
  `Record`. Tests **stub the global `fetch`** (`spyOn(window,'fetch')`) returning a scripted `ReadableStream`
  (HttpTestingController does NOT intercept fetch); a pending stream that `controller.error`s on the abort
  signal drives the stop test. Backend hardening in `ChatEndpoints`: a keep-alive comment every
  `Rag:StreamHeartbeatSeconds` (all writes serialized by a `SemaphoreSlim`) + client-disconnect cancellation.
  **TestServer gotcha:** cancelling the send token alone does NOT trigger `RequestAborted` — **dispose** the
  response/stream to simulate a client disconnect (`FakeStreamingAnswerGenerator.CancellationObserved`).
