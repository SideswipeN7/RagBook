# Tasks: Zadanie pytania z RAG — streaming backend (US-14)

**Input**: Design documents from `specs/010-us14-rag-ask/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/ask-sse.md, quickstart.md

**Tests**: Included — the constitution mandates Test-First (Red→Green→Refactor). Every behavior lands via a
failing test first, at the cheapest tier that proves it (Application for the pipeline/prompt with mocked
retriever+generator; Testcontainers Integration for the SSE endpoint over real pgvector with a fake
streaming generator, and a canned-stream test for the real generator). No test hits Anthropic (§V).

**Organization**: Grows the `Chat` module (US-13 retrieval → full ask→stream pipeline), reusing US-13
(retrieval) + US-02 (key guard). One Setup phase (config + errors), one Foundational phase (value types +
seams + PromptBuilder + fake generator), then the stories: US1 = grounded streamed answer (AC-1/6) 🎯 MVP,
US2 = scope isolation (AC-2), US3 = insufficient grounding (AC-3), US4 = configurable (AC-4), US5 = provider
errors + guards (AC-5). No migration, no frontend (US-15).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel (different files, no incomplete dependency).
- Paths follow plan.md (`src/RagBook/Modules/Chat`, `src/RagBook.Infrastructure/SharedContext/Providers/Anthropic`, `src/RagBook.API`, `tests/…`).

---

## Phase 1: Setup

- [X] T001 [P] Extend `RagOptions` (`src/RagBook/Modules/Chat/RagOptions.cs`): add `SimilarityThreshold` (0.75) and `MaxContextChars` (8000); add both to the `"Rag"` section in `src/RagBook.API/appsettings.json`. (`RagOptions` is already bound in `Program.cs` from US-13.)
- [X] T002 [P] Extend `AnthropicOptions` (`src/RagBook.Infrastructure/SharedContext/Providers/Anthropic/AnthropicOptions.cs`): add `GenerationModel` (`claude-sonnet-5`) and `MaxOutputTokens` (1024); add to the `"Anthropic"` section in `appsettings.json`.
- [X] T003 [P] Extend `ChatErrors` (`src/RagBook/Modules/Chat/Errors/ChatErrors.cs`): `InvalidQuestion` (`chat.invalid_question`, Validation→400), `ProviderRateLimited` (`chat.provider_rate_limited`, RateLimited→429), `ProviderUnavailable` (`chat.provider_unavailable`, Unavailable→503). (429/503 `ErrorType`s already exist from US-02.)

**Checkpoint**: Solution builds; new options bind; the new codes exist.

---

## Phase 2: Foundational (value types + seams — BLOCK the stories)

- [X] T004 [P] Domain value types in `src/RagBook/Modules/Chat/Domain/`: `GroundingPassage` (Number, DocumentId, FileName, PageNumber, Text), `GroundedContext` (Sources + Prompt), `AskOutcome` (`Answerable(GroundedContext)` | `InsufficientGrounding`), `AnswerGenerationFailure` (InvalidKey/RateLimited/Unavailable) + `AnswerGenerationException`, and `GroundingPrompt` (the system-prompt template + a named `RefusalPhrase` constant, commented as the US-17 contract).
- [X] T005 [P] Seams in `Chat/Domain/`: `IPromptBuilder.Build(question, IReadOnlyList<RetrievedChunk>) → GroundedContext`; `IAnswerGenerator.GenerateAsync(GroundedContext, ct) → IAsyncEnumerable<string>`; `IAskQuestionPipeline.PrepareAsync(question, ChatScope, ct) → Task<Result<AskOutcome>>`.
- [X] T006 [P] Application test (Red): `PromptBuilderTests` — numbers passages `[1..K]` most-relevant-first with file+page; the prompt carries the grounding instructions (only-from-passages, cite `[n]`, `RefusalPhrase`, **and the "answer in the question's language" instruction — A2**); trims to `MaxContextChars` by dropping the weakest whole passages — in `tests/RagBook.Application.Tests/Chat/PromptBuilderTests.cs`.
- [X] T007 Implement `PromptBuilder : IPromptBuilder` (`Chat/Domain/PromptBuilder.cs`, uses `IOptions<RagOptions>` for `MaxContextChars`) + register DI (Green for T006).
- [X] T008 [P] Test double `FakeStreamingAnswerGenerator` (scripted deltas; can throw a scripted `AnswerGenerationFailure` **before** or **after** the first delta; records whether it was invoked) in `tests/RagBook.Api.IntegrationTests/Chat/Fakes/`.

**Checkpoint**: value types + seams compile; PromptBuilder green; the fake generator can script streams + failures.

---

## Phase 3: User Story 1 — Grounded answer, streamed (Priority: P1) 🎯 MVP

**Goal**: A question answered by in-scope documents streams a grounded answer: `sources` → `token`s → `done{groundsFound:true}`.

**Independent test**: Seed a ready document answering the question; `POST /api/chat/ask` (fake generator echoing context) → SSE emits sources, then incremental tokens, then done.

- [X] T009 [P] [US1] Application tests (Red): `AskQuestionPipelineTests` — `Should_ReturnAnswerable_When_MatchesAboveThreshold`, `Should_ReturnInsufficient_When_AllBelowThreshold`, `Should_ReturnInsufficient_When_EmptyScope`, `Should_PropagateScopeNotFound`, `Should_ReturnInvalidQuestion_When_EmptyOrTooLong` (mocked `IScopedRetriever` + `IPromptBuilder`) — in `tests/RagBook.Application.Tests/Chat/AskQuestionPipelineTests.cs`.
- [X] T010 [US1] Implement `AskQuestionPipeline : IAskQuestionPipeline` (`Chat/Features/AskQuestion/`): validate (non-empty, ≤ 2000 → `chat.invalid_question`, handler-owned — NOT FluentValidation) → `IScopedRetriever.RetrieveAsync` (propagate `scope_not_found`; `IsEmptyScope` → `InsufficientGrounding`) → threshold filter (`distance ≤ 1 − SimilarityThreshold`; none → `InsufficientGrounding`) → `IPromptBuilder.Build` → `Answerable`; register DI (Green for T009).
- [X] T011 [US1] Integration test (Red→Green): `Should_StreamSourcesThenTokensThenDone` — seed a ready document + a session with a key; the fake generator scripts **≥2 deltas**; `POST /api/chat/ask` (`ConfigureTestServices`) → `Content-Type: text/event-stream`, events in order `sources` → **≥2 `token` events** (proving incremental streaming, not one block — A1/SC-004) → `done{groundsFound:true}` — in `tests/RagBook.Api.IntegrationTests/Chat/AskQuestionEndpointTests.cs`.
- [X] T012 [US1] Implement `ChatEndpoints` `POST /api/chat/ask` (`src/RagBook.API/Endpoints/`): key guard via `IAnthropicClientFactory` → 401 `settings.api_key_missing`; `IAskQuestionPipeline.PrepareAsync` → on failure `ProblemResults.Problem`; on `InsufficientGrounding` → SSE `done{groundsFound:false}`; on `Answerable` → **peek first delta** (failure before it → `ProblemResults.Problem` with mapped code; else write `sources`, the buffered delta + rest as `token`s, then `done{true}`; a later failure → `error` event). Add `ChatContracts` DTOs (`AskQuestionRequest` + `ScopeDto` → `ChatScope`; SSE payloads) + `MapChatEndpoints()` in `Program.cs`.

**Checkpoint**: AC-1/AC-6 demonstrable — a grounded answer streams incrementally. MVP.

---

## Phase 4: User Story 2 — Scope & session isolation (Priority: P1)

**Goal**: No passage from outside the scope or another session ever reaches `sources`/the prompt.

**Independent test**: Seed similar content in-scope, out-of-scope, and in another session; ask in scope → `sources` are all in-scope, current-session.

- [X] T013 [US2] Integration test (Red→Green): `Should_GroundOnlyOnInScopeSessionPassages` — reuse `ChatRetrievalSeed` to seed in/out-of-scope + other-session ready docs; ask in the folder scope; assert the `sources` event lists only in-scope, current-session documents — in `AskQuestionEndpointTests.cs`.

**Checkpoint**: AC-2 — the grounding boundary holds end-to-end.

---

## Phase 5: User Story 3 — Insufficient grounding (Priority: P1)

**Goal**: Below-threshold or empty scope ⇒ no provider call ⇒ `done{groundsFound:false}`.

**Independent test**: Ask an unrelated question (all matches below threshold) → generator never invoked, `done{groundsFound:false}`.

- [X] T014 [US3] Integration test (Red→Green): `Should_ReturnGroundsFalse_And_NotInvokeGenerator_When_AllBelowThreshold` (assert `FakeStreamingAnswerGenerator.Invoked == false`) and `Should_ReturnGroundsFalse_When_ScopeAllProcessing` — in `AskQuestionEndpointTests.cs`.

**Checkpoint**: AC-3 — no grounds ⇒ no generation, deterministic.

---

## Phase 6: User Story 4 — Tunable without code (Priority: P1)

**Goal**: `SimilarityThreshold`, `MaxContextChars`, `TopK` are config; changing them changes behavior.

**Independent test**: With a strict vs loose `SimilarityThreshold`, the same matches yield insufficient vs answerable; with a small `MaxContextChars`, the weakest passages are dropped.

- [X] T015 [US4] Application tests (Red→Green): `Should_DropWeakestPassages_When_ExceedingMaxContextChars` (PromptBuilder with a tiny `MaxContextChars`) and `Should_FlipAnswerableVsInsufficient_When_ThresholdChanged` (AskQuestionPipeline with two `RagOptions` values over the same mocked matches) — in `PromptBuilderTests.cs` / `AskQuestionPipelineTests.cs`.

**Checkpoint**: AC-4 — behavior follows config, no code change.

---

## Phase 7: User Story 5 — Provider failures & guards (Priority: P1)

**Goal**: Each failure mode surfaces its distinct code; guards fire before generation.

**Independent test**: No key → 401 before any provider call; a fake failing before the first delta → ProblemDetails with the mapped code; failing mid-stream → `error` event; empty/over-long question → 400.

- [X] T016 [P] [US5] Integration tests (Red→Green): `AskQuestionErrorTests` — `Should_Return401_When_NoKey` (no generation attempted), `Should_Return400_When_QuestionEmptyOrTooLong`, `Should_ReturnProblemDetails_When_GeneratorFailsBeforeFirstDelta` (Theory: InvalidKey→400 `settings.invalid_api_key`, RateLimited→429 `chat.provider_rate_limited`, Unavailable→503 `chat.provider_unavailable`), `Should_EmitErrorEvent_When_GeneratorFailsMidStream` — in `tests/RagBook.Api.IntegrationTests/Chat/AskQuestionErrorTests.cs`.
- [X] T017 [P] [US5] Integration test (Red→Green): `AnthropicAnswerGeneratorTests` — a canned `HttpMessageHandler` returns an Anthropic-style SSE body → deltas parsed in order; `401/403`→InvalidKey, `429`→RateLimited, `5xx`/thrown→Unavailable — in `tests/RagBook.Api.IntegrationTests/Chat/AnthropicAnswerGeneratorTests.cs` (no real Anthropic).
- [X] T018 [US5] Implement `AnthropicAnswerGenerator : IAnswerGenerator` (`SharedContext/Providers/Anthropic/`): resolve the key via `IAnthropicClientFactory`, `POST /v1/messages` `stream:true` read with `HttpCompletionOption.ResponseHeadersRead`, parse `content_block_delta.text` → yield, map non-2xx / `error` event / transport failure → `AnswerGenerationException`; register DI + the named HttpClient. **Streaming resilience (C1):** the generation `HttpClient` MUST NOT use a standard total-request-timeout/retry that would truncate or re-issue a live stream — either omit `AddStandardResilienceHandler` (use only a connection/attempt timeout) or configure it with `TotalRequestTimeout` disabled and `Retry.MaxRetryAttempts = 0`; `HttpClient.Timeout = InfiniteTimeSpan` (per-read cancellation via the request token). Add a note in `AnthropicOptions` (e.g. no total timeout for streaming). (Green for T017.)

**Checkpoint**: AC-5 — distinct codes pre-stream (ProblemDetails) and mid-stream (`error`), guards before generation.

---

## Phase 8: Polish & cross-cutting

- [X] T019 [P] Docs: add a **"Pipeline RAG (US-14)"** section to `README.md` (ask → validate → key guard → retrieve (US-13) → threshold → trim → grounding prompt → **streamed** answer over SSE; distinct provider error codes; `Rag`/`Anthropic` config; deliberate simplifications: no re-ranking / hybrid / query rewriting) with a small pipeline diagram; record durable notes in `AGENTS.md` (`Chat` ask pipeline; `IAnswerGenerator` streaming seam + fake; endpoint-driven SSE + peek-first-delta boundary; reuses US-13/US-02; codes).
- [X] T020 Full green run: `dotnet test tests/RagBook.Application.Tests` and `dotnet test tests/RagBook.Api.IntegrationTests` (Testcontainers); then the critical-analysis pass on the diff before opening the PR (repo memory: critical-analysis-before-pr). If Smart App Control blocks local test hosts, push and let CI be the green gate.

---

## Dependencies & execution order

- **Setup (T001–T003)** → **Foundational (T004–T008)** block the stories.
- **US1 (T009–T012)** builds the pipeline + SSE endpoint (MVP). **US2 (T013)**, **US3 (T014)** assert isolation + insufficient over that endpoint. **US4 (T015)** asserts config at the Application tier. **US5 (T016–T018)** adds the error/guard behavior and the real generator (tested via a canned stream).
- Within a story, tests precede implementation; `[P]` tasks touch different files.
- Polish (T019–T020) after the stories are green.

## MVP scope

**US1 (T001–T012)** yields the demonstrable increment: `POST /api/chat/ask` returns a grounded, streamed answer (`sources` → `token`s → `done`) over real pgvector retrieval with a fake generator. US2–US5 add scope isolation, the no-grounds short-circuit, config tunability, and the provider-error/guard contract (plus the real streaming Anthropic client, tested offline).
