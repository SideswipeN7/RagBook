# Implementation Plan: Zadanie pytania z RAG — streaming backend (US-14)

**Branch**: `010-us14-rag-ask` (git: `fm/us14-rag-ask`) | **Date**: 2026-07-12 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/010-us14-rag-ask/spec.md`

## Summary

Extend the **`Chat`** module with the streaming RAG answer pipeline. `POST /api/chat/ask` (question + scope in the body) runs: **validate** (non-empty, ≤ 2000 → `chat.invalid_question`) → **key guard** (US-02 `IAnthropicClientFactory` → `settings.api_key_missing` before any provider call) → **retrieve** (reuse US-13 `IScopedRetriever`; `chat.scope_not_found`, empty-scope short-circuit) → **threshold** (drop passages below `SimilarityThreshold`; none left → deterministic *insufficient grounding*, no provider call) → **trim** context to `MaxContextChars` (weakest first) → **build grounding prompt** (numbered `[1]..[K]` with file+page, refuse-if-unsupported, answer in the question's language) → **stream** the answer over `text/event-stream` with typed events `sources` / `token` / `done` / `error`. Generation sits behind `IAnswerGenerator` (real streaming Anthropic client via a thin resilient `HttpClient` to `/v1/messages` `stream:true`; tests swap a deterministic streaming fake — no test hits Anthropic). Provider failures map to distinct codes (`settings.invalid_api_key`, `chat.provider_rate_limited` 429, `chat.provider_unavailable` 503) — a failure **before the first delta** becomes a `ProblemDetails` (headers not yet sent), a failure **mid-stream** becomes an SSE `error` event. `RagOptions` grows `SimilarityThreshold` + `MaxContextChars`. No new DB entity, **no migration** (stateless ask; persistence is US-18). No frontend (US-15).

## Technical Context

**Language/Version**: C# (net10.0, LangVersion preview).

**Primary Dependencies**: reuse US-13 `IScopedRetriever` (retrieval) + US-02 `IAnthropicClientFactory` (key guard) + US-06 `IEmbeddingProvider` (inside the retriever); a thin resilient named `HttpClient` to Anthropic `/v1/messages` (`stream:true`) for generation (mirrors US-02's validation client; `AddStandardResilienceHandler`). SSE is written manually (`Response.WriteAsync("event: …\ndata: …\n\n")` + flush) — the repo's established pattern (US-06 `DocumentStatusEndpoints`). No new NuGet package.

**Storage**: PostgreSQL + pgvector — **read-only** via US-13. No new table, **no migration** (persistence is US-18).

**Testing**: xUnit + FluentAssertions + NSubstitute (Application: pipeline, prompt builder, threshold/trim, validation); Testcontainers `pgvector/pgvector:pg17` (Integration: the `POST /api/chat/ask` SSE endpoint end-to-end with a **fake streaming `IAnswerGenerator`** over seeded data — sources/token/done order, AC-2 scope isolation, AC-3 insufficient, AC-5 error modes, invalid-question, api-key-missing) + a canned-`HttpMessageHandler` test for the real `AnthropicAnswerGenerator` SSE parsing/error-mapping. No test hits Anthropic (§V).

**Target Platform**: Linux container (GCP Cloud Run), stateless streaming endpoint.

**Project Type**: Web (modular-monolith .NET backend); no frontend in US-14 (US-15).

**Performance Goals**: First `sources` event immediately after retrieval; first `token` as soon as the provider yields (true streaming, not buffered).

**Constraints**: Question never in the URL (privacy). No test hits the real provider. All RAG params config-driven. Session isolation + ready-only inherited from US-13.

**Scale/Scope**: One question → one bounded retrieval + one streamed completion.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Assessment |
|---|---|
| I. Vertical-slice modular monolith | ✅ Extends `Modules/Chat/` (`Features/AskQuestion`, `Domain/`, `Errors/`). Generation impl in Infrastructure. The **API endpoint** composes the key guard (Settings seam) + the pipeline (Chat) — composition at the web layer, not a Core→Settings reference. The generator (Infra) resolves the key via the US-02 factory (Infra→Infra). No Core cross-module reference. |
| II. CQRS + Result contract | ✅ The pre-generation pipeline returns `Result<AskOutcome>` (closed `ChatErrors` + reused `SettingsErrors`). Streaming does not fit a single-value command, so the endpoint drives the SSE directly (as US-06's status stream does) — documented deviation, not a violation (no user-facing operation is a Wolverine command here). |
| III. Data isolation by session | ✅ Retrieval is US-13 (explicit session filter); the key guard is session-scoped (US-02). No new data path. |
| IV. Test-First (Red→Green) | ✅ Application tests for the pipeline/prompt/threshold/validation (DB + generator mocked); Integration for the SSE contract over real pgvector with a fake generator; a canned-stream test for the real generator's parsing. |
| V. External providers — resilience + cache | ✅ Generation behind `IAnswerGenerator`; real impl on a resilient `HttpClient` (timeout/retry/circuit-breaker); **tests swap a deterministic streaming fake** — no test hits Anthropic. One embedding model (US-06) governs retrieval. |
| VI. Auditing & time | ✅ No writes (stateless ask). N/A. |
| VII. Secrets | ✅ The BYOK key stays in the session store (US-02); the generator reads it via the factory, never logs it; the question is never placed in the URL. |
| VIII. Operations & delivery | ✅ No migration. `RagOptions` (TopK/threshold/MaxContextChars) + `AnthropicOptions` (model/max-tokens) config-driven. CI runs all tiers. |
| IX. Frontend & design system | ✅ N/A — no UI in US-14 (US-15). |

**Deviation (justified, not a violation)** → the streaming answer is driven directly from the endpoint (SSE), not a Wolverine command/`Result` round-trip, because a token stream is not a single-value result — the same pattern the merged US-06 status stream uses. See Complexity Tracking.

## Project Structure

### Documentation (this feature)

```text
specs/010-us14-rag-ask/
├── plan.md · research.md · data-model.md · quickstart.md
├── contracts/ask-sse.md      # POST /api/chat/ask event contract + the IAnswerGenerator seam
└── tasks.md                  # (/speckit-tasks)
```

### Source Code (repository root)

```text
src/RagBook/Modules/Chat/
├── RagOptions.cs                                   # EXTEND: SimilarityThreshold (0.75), MaxContextChars (8000)
├── Errors/ChatErrors.cs                            # EXTEND: InvalidQuestion(400), ProviderRateLimited(429), ProviderUnavailable(503)
├── Domain/
│   ├── GroundingPassage.cs                         # Number [n], DocumentId, FileName, PageNumber, Text
│   ├── GroundedContext.cs                          # Sources (passages) + the built Prompt string
│   ├── AskOutcome.cs                               # Answerable(GroundedContext) | InsufficientGrounding
│   ├── GroundingPrompt.cs                          # the grounding system-prompt template (commented contract; refusal phrase)
│   ├── IPromptBuilder.cs / PromptBuilder.cs        # (question, passages) → GroundedContext (number, trim to MaxContextChars weakest-first)
│   ├── IAnswerGenerator.cs                         # IAsyncEnumerable<string> GenerateAsync(GroundedContext, ct); throws AnswerGenerationException
│   ├── AnswerGenerationException.cs                # + AnswerGenerationFailure { InvalidKey, RateLimited, Unavailable }
│   └── IAskQuestionPipeline.cs                     # PrepareAsync(question, ChatScope, ct) → Result<AskOutcome>
└── Features/AskQuestion/
    ├── AskQuestionPipeline.cs                      # validate → retrieve (US-13) → threshold → build; Result<AskOutcome>
    └── (validation inline — handler-owned code for the stable chat.invalid_question, NOT FluentValidation)

src/RagBook.Infrastructure/SharedContext/Providers/Anthropic/
├── AnthropicOptions.cs                             # EXTEND: GenerationModel, MaxOutputTokens
└── AnthropicAnswerGenerator.cs                     # IAnswerGenerator: POST /v1/messages stream:true, parse SSE deltas,
                                                    #   map 401→InvalidKey / 429→RateLimited / 5xx·timeout·overloaded→Unavailable
# DI: AddScoped<IAskQuestionPipeline, AskQuestionPipeline>(), AddScoped<IPromptBuilder,…>,
#     AddScoped<IAnswerGenerator, AnthropicAnswerGenerator>() + AddHttpClient(generation) .AddStandardResilienceHandler()

src/RagBook.API/
├── Endpoints/ChatEndpoints.cs                      # POST /api/chat/ask: key guard → pipeline → SSE (sources/token/done/error);
│                                                    #   pre-first-delta failure → ProblemDetails; mid-stream → error event
├── Endpoints/ChatContracts.cs                      # AskQuestionRequest { Question, ScopeDto {Type, TargetId} } + SSE payload DTOs
└── Program.cs                                       # MapChatEndpoints(); Configure<RagOptions> already bound (US-13)

tests/
├── RagBook.Application.Tests/Chat/                 # AskQuestionPipeline (validate/threshold/insufficient/scope), PromptBuilder (numbering/trim)
└── RagBook.Api.IntegrationTests/Chat/
    ├── AskQuestionEndpointTests.cs                 # SSE end-to-end w/ FakeStreamingAnswerGenerator over seeded pgvector
    ├── AskQuestionErrorTests.cs                    # AC-5 modes (pre-token ProblemDetails vs mid-stream error), invalid-question, api-key-missing
    ├── AnthropicAnswerGeneratorTests.cs            # canned HttpMessageHandler → SSE parse + error mapping (no real Anthropic)
    └── Fakes/FakeStreamingAnswerGenerator.cs
```

**Structure Decision**: Web modular-monolith. US-14 grows the `Chat` module from the US-13 retrieval engine into the full ask→stream pipeline, reusing US-13 (retrieval) and US-02 (key guard) rather than duplicating them, and adds the first **streaming external provider** (generation) behind `IAnswerGenerator` (thin resilient `HttpClient`, mirroring US-02's validation client). The SSE endpoint is driven directly (US-06 status-stream pattern). No migration, no frontend.

## Complexity Tracking

| Deviation | Why needed | Simpler alternative rejected because |
|---|---|---|
| Endpoint-driven SSE (not a Wolverine command/`Result`) | A token stream is not a single-value result; the answer must flush incrementally. The pre-generation pipeline still returns `Result<AskOutcome>` — only the token stream bypasses the command bus. | Wolverine `InvokeAsync<T>` returns one value; buffering the whole answer to fit it would defeat streaming (FR-008). The merged US-06 status stream already sets this SSE-from-endpoint precedent. |
| Key guard resolved at the API endpoint (references Settings' `IAnthropicClientFactory`) | The pre-generation `api_key_missing` must be a `ProblemDetails` before the stream starts; the endpoint is the composition boundary that already wires all modules. | A Core `Chat`→`Settings` reference would violate §I; a bespoke Chat guard seam duplicates US-02's factory. The generator (Infra) reuses the same factory Infra→Infra. |
