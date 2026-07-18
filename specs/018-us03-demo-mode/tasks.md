# Tasks: Tryb demo — demo mode (US-03)

**Input**: Design documents from `specs/018-us03-demo-mode/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/demo-mode.md, quickstart.md

**Tests**: REQUIRED (constitution §IV; standing rule — all 4 tiers green before any PR).

**Organization**: Grouped by user story. A shared demo core (Modules/Demo + Chat demo scope + app key + seeder)
lives in **Foundational**; each story phase then stays thin.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: different file, no dependency on an incomplete task → parallelizable.
- **[Story]**: US1 (keyless demo answer), US2 (per-session limit), US3 (per-IP limit), US4 (read-only), US5 (quota).

---

## Phase 1: Setup

- [x] T001 Confirm branch `fm/us03-demo-mode` off master (US-12 merged `b77209f`); no new packages (reuses Wolverine, EF, IMemoryCache, CDK).

---

## Phase 2: Foundational (Blocking Prerequisites — the shared demo core)

### Modules/Demo — options, constants, errors, seams

- [x] T002 [P] `DemoConstants` (`static readonly Guid DemoSessionId`) in `src/RagBook/Modules/Demo/DemoConstants.cs`.
- [x] T003 [P] `DemoOptions` (`MaxQuestionsPerSession=10`, `MaxQuestionsPerIpPerHour=20`, `SessionCounterTtlHours=24`, `Documents: DemoDocumentManifest[]`) SectionName `"Demo"` in `src/RagBook/Modules/Demo/DemoOptions.cs`; bind in `Program.cs`.
- [x] T004 [P] `DemoErrors` (`LimitReached` = RateLimited `chat.demo_limit_reached`; `Unavailable` = Unavailable `chat.demo_unavailable`) in `src/RagBook/Modules/Demo/Errors/DemoErrors.cs`.
- [x] T005 [P] Seams: `IDemoDocumentSeeder`, `IDemoQuestionCounter` (`Remaining`/`Asked`/`TryConsume`), `IDemoIpThrottle` (`TryRegister(ip) → (Allowed, RetryAfterSeconds)`) in `src/RagBook/Modules/Demo/Domain/`.

### Chat demo scope + key source

- [x] T006 [P] `ChatScopeType.Demo = 3` + `ChatScope.Demo()` in `src/RagBook/Modules/Chat/Domain/{ChatScopeType,ChatScope}.cs`.
- [x] T007 [P] `GroundedContext` + `bool IsDemo = false` in `src/RagBook/Modules/Chat/Domain/GroundedContext.cs`; `AskQuestionPipeline` sets it (`scope.Type == Demo`).
- [x] T008 [P] `Document.CreateDemo(Guid id, long sizeBytes, string fileName, string contentType, string storagePath, DateTimeOffset uploadedAt)` fixed-id `Origin=Demo` factory in `src/RagBook/Modules/Documents/Domain/Document.cs`.

### Provider — application key

- [x] T009 `AnthropicOptions` + `string? ApplicationKey`; `IAnthropicClientFactory` + `CreateForDemo()`; `AnthropicClientFactory.CreateForDemo()` (app key from options, else `DemoErrors.Unavailable`); `AnthropicAnswerGenerator` picks `context.IsDemo ? CreateForDemo() : CreateForSession()`. Files under `src/RagBook.Infrastructure/SharedContext/Providers/Anthropic/` + `src/RagBook/Modules/Settings/Domain/IAnthropicClientFactory.cs`.

### Infrastructure — counter, throttle, seeder + DI

- [x] T010 [P] `MemoryCacheDemoQuestionCounter` (IMemoryCache `demo-questions:{session}`, sliding TTL = `SessionCounterTtlHours`) in `src/RagBook.Infrastructure/SharedContext/Demo/`.
- [x] T011 [P] `MemoryCacheDemoIpThrottle` (IMemoryCache `demo-ip:{ip}`, hourly fixed window, `TimeProvider`) in `src/RagBook.Infrastructure/SharedContext/Demo/`.
- [x] T012 `DemoDocumentSeeder` in `src/RagBook.Infrastructure/SharedContext/Demo/`: for each manifest id, in a scope with `ISessionInitializer = DemoSessionId`, if absent (`IgnoreQueryFilters`) → save blob + `CreateDemo` + run US-06 processing (extract→chunk→embed→MarkReady). Register all Demo seams in `DependencyInjection.cs`; run `SeedAsync` at startup in `Program.cs`.
- [x] T013 `ScopedRetriever` demo branch: `scope.Type == Demo` → skip session/target validation; `ScopeHasReadyChunksAsync`/`SearchAsync` use `WHERE d.origin = @demoOrigin AND d.status = @ready` (no session predicate). File `src/RagBook.Infrastructure/SharedContext/Retrieval/ScopedRetriever.cs`.

**Checkpoint**: demo core compiles; a demo ask can retrieve + generate on the app key. Stories layer on top.

---

## Phase 3: User Story 1 — Keyless demo answer (P1) 🎯 MVP

**Goal**: a keyless session picks demo scope → streamed answer + citations from demo docs on the app key; a
read-only Demo section lists them.

- [x] T014 [P] [US1] Application test `AnthropicAnswerGenerator`/pipeline demo-key selection + `AskQuestionPipeline` sets `IsDemo` (`tests/RagBook.Application.Tests/...`). (FAIL first.)
- [x] T015 [US1] `ChatEndpoints`: `TryBuildScope` maps `"demo"` → `ChatScope.Demo()`; for demo scope **skip** the `CreateForSession` 401 guard and instead run the demo guard chain (T018/T020 wire the limits; here at minimum resolve `CreateForDemo` → `chat.demo_unavailable` 503 when unset, then stream).
- [x] T016 [P] [US1] `TreeReader`/`TreeData`/`TreeResponse` + a global `DemoDocuments` read (`IgnoreQueryFilters`, `Origin==Demo`, AsNoTracking); `GET /api/tree` returns `demo[]`.
- [x] T017 [P] [US1] Integration test: seeder idempotent (clean DB seeds; 2nd run no-op) + demo docs visible cross-session (`GET /api/tree` `demo[]` from a fresh session) + a demo ask **without a session key** streams an answer with ≥1 demo citation. `tests/RagBook.Api.IntegrationTests/Demo/DemoEndpointTests.cs`.
- [x] T018 [US1] Frontend: `tree.store.ts` `demoDocuments` signal; read-only **Demo section** in `document-tree.*` (badge, no move/delete/checkbox); "Dokumenty demo" scope option + demo banner in chat. Karma: section renders read-only; scope option posts `{ type: "demo" }`.

**Checkpoint**: keyless demo Q&A works end-to-end.

---

## Phase 4: User Story 2 — Per-session question limit (P1)

**Goal**: after N demo questions the next is refused (`429 chat.demo_limit_reached`); UI shows "X / N" + BYOK nudge.

- [x] T019 [P] [US2] Application test `MemoryCacheDemoQuestionCounter`: consumes to the cap, `Remaining` decrements, `TryConsume` false past the cap. (FAIL first.)
- [x] T020 [US2] `ChatEndpoints` demo guard: after the IP check, `counter.TryConsume()` — at/over cap → `DemoErrors.LimitReached` (429), no generation; a successful ask consumes one. `DemoEndpoints`: `GET /api/demo/status` → `{ asked, max, remaining, available }`.
- [x] T021 [P] [US2] Integration test: N demo asks succeed, the N+1 → `429 chat.demo_limit_reached` and no generation; `GET /api/demo/status` reflects the count.
- [x] T022 [US2] Frontend `demo.store.ts` (`remaining`/`max`/`available` from `/api/demo/status`; decrement on ask; `isExhausted`); chat shows "X / N pytań demo" + BYOK nudge on exhaustion; maps `chat.demo_limit_reached`. Karma: counter + nudge.

**Checkpoint**: per-session limit enforced + surfaced.

---

## Phase 5: User Story 3 — Per-IP hourly rate limit (P2)

**Goal**: an IP over the hourly demo rate → `429 + Retry-After`; UI shows a readable retry message.

- [x] T023 [P] [US3] Application test `MemoryCacheDemoIpThrottle`: allows up to the hourly cap, then denies with a positive `RetryAfterSeconds` (deterministic via a fake `TimeProvider`). (FAIL first.)
- [x] T024 [US3] `ChatEndpoints` demo guard: `ipThrottle.TryRegister(remoteIp)` FIRST — not allowed → write `429` + `Retry-After: <seconds>` header, no counter/generation.
- [x] T025 [P] [US3] Integration test: exceed the per-IP hourly demo cap → `429` with a `Retry-After` header; a BYOK (non-demo) ask from the same IP is NOT throttled.
- [x] T026 [US3] Frontend maps the `429`/`Retry-After` to a readable "spróbuj ponownie później" message. Karma.

**Checkpoint**: per-IP throttle enforced + surfaced.

---

## Phase 6: User Story 4 — Read-only demo documents (P1)

**Goal**: no session can delete/move/bulk a demo doc; the UI offers no mutating controls.

- [x] T027 [P] [US4] Integration test: `DELETE /api/documents/{demoId}`, `PATCH .../folder`, `POST bulk-move|bulk-delete` including a demo id → `document.read_only` (409 / 422 failures); the demo doc is unchanged. Verify the single-DELETE path guards demo — if not, close the gap minimally (T028).
- [x] T028 [US4] IF a gap is found: add the `Origin==Demo → document.read_only` guard to the single delete path (`DeleteDocument` slice / repository). Otherwise mark no-change-needed with a note.
- [x] T029 [US4] Frontend: the Demo section renders no move/delete/checkbox controls (assert in the Karma from T018); confirm bulk selection never includes demo docs.

**Checkpoint**: read-only proven against real seeded demo docs.

---

## Phase 7: User Story 5 — Demo excluded from quota (P2)

**Goal**: demo docs never count toward the user's quota; a full session still uploads its whole allowance.

- [x] T030 [P] [US5] Integration regression test: with demo docs seeded, a session's quota (`GET /api/quota`) counts 0 demo docs and a fresh session can upload up to `MaxDocuments`.

**Checkpoint**: quota exclusion proven with real demo docs.

---

## Phase 8: Polish & Cross-Cutting

- [x] T031 [P] Add a demo section to `docs/features/README.md` (keyless demo, app key, limits) per DoD; note the GIF is manual/out-of-scope for automation.
- [x] T032 [P] Ensure `appsettings` has a `Demo` section (limits + document manifest) and the application key is read from env/secret (never committed).
- [x] T033 Run all 4 tiers green (Domain/Application/Integration-Testcontainers/Angular-Karma) per quickstart.md; then critical diff review before the PR.

---

## Dependencies & Execution Order

- **Setup (T001)** → **Foundational (T002–T013)** blocks everything. Within Foundational: T002–T008 (distinct new files) parallel; T009 (provider) and T010/T011 (counter/throttle) parallel; T012 (seeder) depends on T003/T008; T013 (retriever) depends on T006.
- **US1 (T014–T018)** depends on Foundational. **US2 (T019–T022)** and **US3 (T023–T026)** add the guard chain to the shared `ChatEndpoints` demo path → sequence the `ChatEndpoints` edits (US1 wires the skeleton, US3 adds the IP check FIRST, US2 the counter next); integration tests parallel where they touch distinct files.
- **US4 (T027–T029)** + **US5 (T030)** are regressions over the seeded demo docs → after the seeder (T012) exists.
- **Polish (T031–T033)** last.

## Parallel Opportunities

- T002/T003/T004/T005/T006/T007/T008 (distinct new files) in parallel.
- Application tests T014/T019/T023 in parallel (distinct files).
- Integration tests share the Demo test file → sequence T017 → T021 → T025 → T027 → T030 (or split into per-story files to parallelize).

## Implementation Strategy

**MVP** = US1 (keyless demo answer) + the read-only Demo section, with US4/US5 regressions proving the safety
contracts. Build the demo core (Foundational) first — seeder, demo retrieval branch, app-key generation — then the
endpoint scope + tree section (US1), then the two limits (US2 counter, US3 IP throttle) layered onto the demo guard
chain, then the read-only + quota regressions, then the README note and the full green run + critical review.
