# Implementation Plan: Tryb demo — demo mode (US-03)

**Branch**: `018-us03-demo-mode` | **Date**: 2026-07-16 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/018-us03-demo-mode/spec.md`

## Summary

A keyless demo experience: a visitor picks the **demo** chat scope and gets a full streaming RAG answer with
citations over a small set of **globally-visible, read-only** demo documents, generated on a **server-held
application key** — bounded by a **per-session lifetime question limit** and a **per-IP hourly rate limit**. Demo
documents are **seeded once at startup** (idempotent, fixed ids) under a fixed sentinel demo-session id, and read by
`Origin == Demo` with the per-session filter bypassed. The whole US-13–US-17 chat pipeline (retrieval, threshold,
grounding, SSE, citations) is reused unchanged; only the **scope** (a new `ChatScopeType.Demo` whose retrieval drops
the session predicate) and the **key source** (application key instead of the session BYOK key) differ. New
`Modules/Demo` owns the cross-cutting demo concern (options, constants, seeder, per-session counter, per-IP
throttle, errors); Chat gains the demo scope + demo key branch; Documents gains a fixed-id demo factory; the tree
gains a read-only demo section. Read-only enforcement (move/delete/bulk) and quota exclusion already exist
(US-10/11/12/05) — this story adds regression coverage.

## Technical Context

**Language/Version**: C# (.NET 10) backend; TypeScript / Angular 20 frontend.

**Primary Dependencies**: the chat pipeline (US-14 `AskQuestionPipeline` / `AnthropicAnswerGenerator` / SSE
`ChatEndpoints`), retrieval (US-13 `ScopedRetriever`, raw-SQL pgvector), the processing pipeline (US-06 chunk +
embed) for seeding, BYOK key handling (US-02 `IAnthropicClientFactory`), `IMemoryCache` throttle pattern (US-02
`MemoryCacheApiKeyThrottle`), the tree read (US-07 `TreeReader`), design tokens.

**Storage**: PostgreSQL — demo documents + chunks are ordinary `documents`/`chunks` rows with `Origin = Demo`,
seeded under a fixed `DemoSessionId`. **No schema change / no migration** (origin + session columns already exist).

**Testing**: xUnit + NSubstitute + FluentAssertions (Application/Integration); Testcontainers for the seeder
(idempotent, cross-session read), demo retrieval without a session key, read-only refusal, quota exclusion, and the
per-IP 429 + `Retry-After`; Karma for the demo tree section, the remaining-questions counter, the demo banner, and
the limit messages.

**Target Platform**: Linux container backend; evergreen browser SPA.

**Project Type**: Web (modular-monolith .NET backend + Angular SPA).

**Performance Goals**: One SSE round-trip per demo ask; seeding is a bounded one-time startup step; both demo limits
are configured (no unbounded application-key spend).

**Constraints**: application key server-only, never exposed (§VII); demo documents global read-only (§III — a shared
resource, not another session's private data); demo retrieval bypasses the per-session predicate but is fenced to
`Origin == Demo`; per-session **lifetime** counter, per-IP **hourly** window; `DemoLimitReached` = `429`
(`chat.demo_limit_reached`), per-IP over-limit = `429 + Retry-After`, application-key-unset / provider failure =
readable "demo unavailable" (never a raw 500); design tokens, no native dialogs, ≥360px.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **I. Vertical-Slice Modular Monolith** ✅ — a new `Modules/Demo` owns the demo concern (`DemoOptions`,
  `DemoConstants`, `IDemoDocumentSeeder`, `IDemoQuestionCounter`, `IDemoIpThrottle`, `DemoErrors`); Chat gets the
  `ChatScopeType.Demo` branch (retriever + generator key source + endpoint); Documents gets a `Document.CreateDemo`
  factory. Infrastructure implements the seams. No module reaches into another's internals — the retriever already
  lives in Infrastructure (sees both), and the demo key source is behind `IAnthropicClientFactory`.
- **II. CQRS + Result Contract** ✅ — demo failures are code-based `Error`s → ProblemDetails: `chat.demo_limit_reached`
  (RateLimited → 429), `chat.demo_unavailable` (Unavailable → 503), reusing `document.read_only` (409) for
  mutation attempts. The per-IP over-limit is a `429 + Retry-After` written directly (a transport concern, like a
  gateway throttle) — justified below.
- **III. Data Isolation** ✅ (with a justified, tested seam) — demo documents are a **shared global resource**, not a
  session's private data. They are seeded under a fixed `DemoConstants.DemoSessionId` (so the stamping interceptor
  is untouched) and read only through **explicitly demo-fenced** paths (`Origin == Demo`, session predicate
  dropped) — the demo retrieval branch and the tree demo query. Every *user* read path is unchanged and still
  session-scoped; a user can never see another **user's** data. Testcontainers proves cross-session demo
  visibility AND that user scopes never leak demo-vs-user across sessions.
- **IV. Test-First** ✅ — Application (counter limit; retriever demo branch selects Origin==Demo; generator uses the
  demo key when `IsDemo`; upload during demo → user), Integration (seed idempotent + cross-session; demo ask without
  a session key returns answer+citations from demo docs; delete/move/bulk demo → read_only; demo excluded from
  quota; per-IP 429 + Retry-After), Angular (read-only demo section w/o mutating controls; remaining counter; demo
  banner; limit/unavailable messages). Red→Green.
- **V. Providers** ✅ — demo generation reuses the resilient Anthropic client; the application key is config
  (`AnthropicOptions.ApplicationKey`, env/secret); all demo limits are `DemoOptions` (no magic numbers).
- **VI. Auditing & Time** ✅ — seed timestamps via `TimeProvider`; counters/throttle windows via `TimeProvider` (no
  `DateTime.Now`).
- **VII. Secrets** ✅ — application key only in configuration/secret store, never in the repo, never sent to the
  client. **VIII. Ops** ✅ — **no migration**; seeder idempotent by fixed id, safe on clean DB + restart.
- **IX. Frontend & Design System** ✅ — a read-only demo section (badge, no move/delete), the "X / N pytań demo"
  counter, a demo banner, a "Dokumenty demo" scope option, and code-mapped limit/unavailable messages; tokens,
  ≥360px, no native dialogs.

**Result: PASS** — the one deviation (per-IP limit written as `429 + Retry-After` in the handler rather than
`AddRateLimiter` middleware) is recorded in Complexity Tracking with its rationale; it is not a constitution
violation.

## Project Structure

### Documentation (this feature)

```text
specs/018-us03-demo-mode/
├── plan.md, research.md, data-model.md, quickstart.md
├── contracts/demo-mode.md
├── checklists/requirements.md
└── tasks.md   (/speckit-tasks)
```

### Source Code (repository root)

```text
src/RagBook/Modules/Demo/
├── DemoOptions.cs                 # MaxQuestionsPerSession=10, MaxQuestionsPerIpPerHour=20, SessionCounterTtlHours, Documents[] manifest — SectionName "Demo"
├── DemoConstants.cs               # DemoSessionId (fixed GUID)
├── Errors/DemoErrors.cs           # chat.demo_limit_reached (429), chat.demo_unavailable (503)
└── Domain/
    ├── IDemoDocumentSeeder.cs     # SeedAsync — idempotent by fixed id
    ├── IDemoQuestionCounter.cs    # Remaining()/TryConsume() per session (lifetime)
    └── IDemoIpThrottle.cs         # TryRegister(ip) → (allowed, retryAfter)

src/RagBook/Modules/Chat/Domain/
├── ChatScopeType.cs               # + Demo = 3
├── ChatScope.cs                   # + Demo() factory
└── GroundedContext.cs             # + bool IsDemo = false (key-source flag)

src/RagBook/Modules/Documents/Domain/Document.cs   # + CreateDemo(Guid id, …) fixed-id Demo factory

src/RagBook.Infrastructure/SharedContext/
├── Demo/
│   ├── DemoDocumentSeeder.cs      # startup: per manifest id, if absent → CreateDemo + blob + chunk/embed (reuse US-06); under DemoSessionId scope
│   ├── MemoryCacheDemoQuestionCounter.cs   # IMemoryCache demo-questions:{session}, TTL = SessionCounterTtlHours
│   └── MemoryCacheDemoIpThrottle.cs         # IMemoryCache demo-ip:{ip}, hourly fixed window
├── Retrieval/ScopedRetriever.cs   # demo branch: WHERE origin=@demo AND status=ready (no session predicate)
├── Persistence/TreeReader.cs      # + global demo-docs read (IgnoreQueryFilters, Origin==Demo)
└── Providers/Anthropic/{AnthropicClientFactory.cs, AnthropicAnswerGenerator.cs, AnthropicOptions.cs}  # + ApplicationKey + CreateForDemo(); generator picks key by context.IsDemo

src/RagBook.API/
├── Endpoints/ChatEndpoints.cs     # "demo" scope; demo guard (IP throttle 429+Retry-After → counter 429 → app-key 503); skip session-key guard for demo
├── Endpoints/DemoEndpoints.cs     # GET /api/demo/status → { asked, max, remaining, available }
└── Program.cs                     # Configure<DemoOptions>; run the seeder at startup

src/Web/src/app/
├── core/demo.store.ts             # remaining-questions signal (GET /api/demo/status; decrement on ask); demo docs from tree
├── core/tree.store.ts             # + demoDocuments signal (from GET /api/tree)
├── documents/tree/document-tree.* # read-only Demo section (badge, no move/delete)
└── chat/…                         # "Dokumenty demo" scope option; demo banner + "X / N pytań demo"; limit/unavailable messages
```

**Structure Decision**: A new `Modules/Demo` owns the demo-specific concern; Chat gains a `Demo` scope whose
retrieval + key source branch on demo-ness; the seeder + counters + throttle live in Infrastructure behind Demo
seams. `GET /api/tree` is extended with a global demo-doc list; a small `GET /api/demo/status` feeds the counter.
No migration.

## Complexity Tracking

| Deviation | Why needed | Simpler alternative rejected because |
|---|---|---|
| Per-IP hourly limit enforced **in the handler** (`IDemoIpThrottle` + a manual `429 + Retry-After`) rather than `AddRateLimiter` middleware | The demo discriminator (scope) is in the **request body**; ASP.NET Core rate-limiter policies run before model binding and can't see it, and a blanket per-IP limit on `/api/chat/ask` would wrongly throttle BYOK users too. | `AddRateLimiter` on the whole ask endpoint throttles non-demo users; a body-aware middleware would duplicate model binding. The in-handler throttle reuses the tested `MemoryCacheApiKeyThrottle` pattern and stays demo-only. |
| `GroundedContext.IsDemo` flag threaded to the generator | The generator resolves its own key; demo needs the application key, not the session key, without changing the `IAnswerGenerator` signature. | A new interface overload/param touches every caller + test; the record flag flows naturally from the pipeline (which knows the scope). |
