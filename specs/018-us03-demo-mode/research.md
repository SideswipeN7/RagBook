# Phase 0 Research — US-03 Tryb demo (demo mode)

## D1 — Demo document ownership vs the session query filter (clarify Q1)

**Decision**: Demo documents are seeded under a fixed **`DemoConstants.DemoSessionId`** (a compile-time constant
GUID). The seeder runs in a DI scope whose `ISessionInitializer` is set to `DemoSessionId`, so the existing
`SessionStampingInterceptor` stamps that id on insert — **the interceptor is not modified**. The **read
discriminator is `Origin == Demo`**, not the session id: demo reads (the retrieval demo branch and the tree demo
query) drop the per-session predicate and select `WHERE origin = Demo`. The sentinel id only keeps seed writes
consistent and gives demo rows a real (non-empty) owner.

**Rationale**: Keeps the shared isolation seam (`SessionStampingInterceptor`, the global query filter) untouched;
no "ownerless" `Guid.Empty` rows. Every *user* read path stays session-scoped and unchanged, so a user can never see
another user's data; only the two explicitly demo-fenced paths bypass the filter, and only for `Origin == Demo`.

**Alternatives rejected**: `UserSessionId = Guid.Empty` + teaching the stamping interceptor to skip `Origin == Demo`
— touches a NON-NEGOTIABLE isolation seam and introduces an ownerless row shape for one feature.

## D2 — Demo-limit shape + counter lifetime (clarify Q2)

**Decision**: `DemoErrors.LimitReached` = `Error.RateLimited("chat.demo_limit_reached", …)` → **429** via the
existing `ErrorStatusMapper` (RateLimited → 429), consistent with `chat.provider_rate_limited` /
`settings.too_many_attempts`. The per-session counter is a **lifetime** count (`IDemoQuestionCounter`, IMemoryCache
keyed `demo-questions:{session}`, a long sliding TTL from `DemoOptions.SessionCounterTtlHours` so it lives as long
as the session). The per-IP limit is a **separate hourly window** (D4).

**Rationale**: Reuses the code-based-ProblemDetails convention (§II) and the existing 429 mapping. A per-session
lifetime cap bounds one visitor's spend on the application key; the frontend branches on the code to show the
counter + BYOK nudge.

## D3 — Application key + demo generation (key source)

**Decision**: Add `AnthropicOptions.ApplicationKey` (bound from config/secret, may be null in dev). Add
`IAnthropicClientFactory.CreateForDemo()` → a handle over the application key, or `DemoErrors.Unavailable` when
unset. Thread demo-ness through generation with a new `GroundedContext.IsDemo` flag (default `false`, set by
`AskQuestionPipeline` when `scope.Type == Demo`); `AnthropicAnswerGenerator` resolves
`context.IsDemo ? CreateForDemo() : CreateForSession()`. The endpoint's **pre-generation guard** branches on scope:
demo → `CreateForDemo().IsFailure ⇒ chat.demo_unavailable` (503) and **skips** the session `CreateForSession()`
guard; non-demo → the existing `settings.api_key_missing` (401) guard, unchanged.

**Rationale**: The generator already self-resolves its key; a record flag keeps the `IAnswerGenerator` signature and
every existing caller/test unchanged while flowing the scope's key choice through. A provider/budget failure
mid-stream stays the existing SSE `error` event / `chat.provider_unavailable` path (the frontend maps it to a
readable "demo unavailable" — never a raw 500, FR-010).

**Alternatives rejected**: a separate `IDemoAnswerGenerator` (duplicates the whole SSE parser); passing the key/handle
through `GenerateAsync` (interface churn across all callers + tests).

## D4 — Demo retrieval branch (drop the session predicate)

**Decision**: Add `ChatScopeType.Demo = 3` + `ChatScope.Demo()`. In `ScopedRetriever`, when `scope.Type == Demo`:
skip the session/target validation, and build both the empty-scope `EXISTS` and the vector search with
`WHERE d.origin = @demoOrigin AND d.status = @ready` (**no** `user_session_id` predicate, `ScopePredicate` = TRUE).
All other scopes keep `WHERE d.user_session_id = @session AND … ({ScopePredicate})` exactly as today. `@demoOrigin`
is a bound parameter (`(int)DocumentOrigin.Demo`); embedding + pgvector cosine search are reused unchanged.

**Rationale**: One additive branch on the existing raw-SQL retriever; the session predicate is dropped **only** for
the demo branch and replaced by the origin fence, so demo search is global-yet-fenced and every user search stays
session-locked.

## D5 — Idempotent startup seeder

**Decision**: `IDemoDocumentSeeder.SeedAsync` runs at startup (a scoped step alongside the existing resource setup).
For each entry in `DemoOptions.Documents` (fixed `Id`, `FileName`, `ContentType`, asset path/inline text), in a DI
scope with `ISessionInitializer` = `DemoSessionId`: if a document with that `Id` already exists (checked with
`IgnoreQueryFilters`), skip; else save the blob, insert `Document.CreateDemo(Id, …)` (new fixed-id factory,
`Origin = Demo`, `Status = Processing`), then run the US-06 processing (extract → chunk → embed → `MarkReady`) so
the demo doc has ready chunks. Idempotent by fixed id ⇒ a no-op on an already-seeded DB and across restarts.

**Rationale**: Reuses the whole US-06 pipeline (chunker + `IEmbeddingProvider` + `IChunkRepository`), so demo docs
are indexed exactly like uploads. Fixed ids make idempotency a cheap existence check and give the tree/retrieval
stable references. `Document.CreateDemo` is the minimal new factory (the existing ones mint a random id and force
`Origin = User`).

**Alternatives rejected**: idempotency by `(origin, file_name)` (fragile if a name changes); seeding raw chunk rows
with hand-made embeddings (diverges from the real indexing path).

## D6 — Per-IP hourly throttle (429 + Retry-After)

**Decision**: `IDemoIpThrottle.TryRegister(ip)` → `(bool Allowed, int RetryAfterSeconds)`, IMemoryCache keyed
`demo-ip:{ip}`, fixed hourly window (limit `DemoOptions.MaxQuestionsPerIpPerHour`), mirroring
`MemoryCacheApiKeyThrottle`. In `ChatEndpoints`, for a **demo** ask only, call it first; when not allowed, write a
`429` with a `Retry-After` header (seconds to the window reset) and stop — before any counter/generation. Client IP
from `HttpContext.Connection.RemoteIpAddress` (respecting forwarded headers if configured). **`AddRateLimiter`
middleware is deliberately not used** (see plan Complexity Tracking — the demo discriminator is in the body).

**Rationale**: Body-aware, demo-only, reuses the tested throttle pattern, and emits the exact `429 + Retry-After`
AC-3 requires. BYOK asks are never throttled.

## D7 — Read-only enforcement + quota exclusion (already done; regression only)

**Decision**: No new enforcement code. `Origin == Demo` is already refused by `MoveDocumentCommandHandler`
(`document.read_only`), `BulkValidation` (bulk move/delete), and single delete flows through the same document
lookups; `DocumentQuotaRepository.QuotaCounting()` already excludes `Origin == Demo`. This story adds **regression
tests** at the integration tier (delete/move/bulk a demo doc → `document.read_only`; demo docs absent from quota)
and, if a gap is found (e.g. single `DELETE /api/documents/{id}` doesn't guard demo), closes it minimally.

**Rationale**: US-10/11/12/05 already implement the guards; US-03's job is to prove them against real seeded demo
docs and surface the read-only state in the UI. **Verify** the single-delete path during implementation — if it
lacks a demo guard, add one (`document.read_only`) as a small in-scope fix.

## D8 — Frontend demo surfaces

**Decision**: `GET /api/tree` gains a `demo` document list (global, `Origin == Demo`); `tree.store.ts` gets a
`demoDocuments` signal and `document-tree.html` renders a **read-only Demo section** (badge, no checkbox / move /
delete controls). A `demo.store.ts` reads `GET /api/demo/status` (`asked` / `max` / `remaining` / `available`) to
show "X / N pytań demo" and decrements on a successful demo ask; the chat scope selector gains a **"Dokumenty
demo"** option (posts `scope: { type: "demo" }`); a demo banner shows while the demo scope is active; the
`chat.demo_limit_reached` (with the BYOK nudge), `429`, and `chat.demo_unavailable` codes map to readable messages.

**Rationale**: Reuses the tree/scope/SSE frontend; the demo section is purely additive and read-only, and the
counter/banner are small signal-driven pieces. Design tokens, ≥360px, no native dialogs.
