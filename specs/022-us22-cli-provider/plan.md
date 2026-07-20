# Implementation Plan: Keyless generation via the local Claude CLI (US-22)

**Branch**: `022-us22-cli-provider` | **Date**: 2026-07-18 | **Spec**: [spec.md](./spec.md)

## Summary

Add `ClaudeCliOptions` (`Enabled`, `Command`, `Model?`, `TimeoutSeconds`; section `ClaudeCli`, off by default), a
`ClaudeCliAnswerGenerator : IAnswerGenerator` that runs `claude -p --output-format text [--append-system-prompt …]
[--model …]` (grounded prompt on stdin) via an `ICliRunner` seam, and a `RoutingAnswerGenerator : IAnswerGenerator`
that picks the backend per request: **key available → the Anthropic generator (unchanged); no key + CLI enabled →
the CLI generator; else → the existing key-missing error**. Relax the `ChatEndpoints` key guards when CLI mode is on,
and expose `GET /api/config { keylessGeneration }` so the frontend composer unlocks. Everything else (retrieval,
grounding, citations, SSE, isolation, demo limits) is unchanged.

## Constitution Check

- **I** ✅ generation is behind `IAnswerGenerator`; the CLI + router live in Infrastructure (Providers).
- **II** ✅ failures map to `chat.provider_unavailable`; no new error shape.
- **III** ✅ no data access change. **IV** ✅ unit tests: `ClaudeCliAnswerGenerator` (via a fake `ICliRunner`),
  `RoutingAnswerGenerator` (key-present → Anthropic, no-key+enabled → CLI, no-key+disabled → throw); integration:
  guard relaxed + `/api/config`. **V** ✅ provider addition, config-driven, off by default; prompt passed safely
  (arg list + stdin, no shell). **VII** ✅ no secret (the CLI carries its own auth). **VIII** ✅ no migration.
- **IX** ✅ the composer unlocks when `keylessGeneration`; no new UI surface.

**Result: PASS.**

## Files

```text
src/RagBook/Modules/Chat/Domain/ICliRunner.cs                 # RunAsync(command,args,stdin,ct) → {ExitCode,StdOut,StdErr}
src/RagBook/Modules/Chat/ClaudeCliOptions.cs                  # Enabled/Command/Model/TimeoutSeconds — section "ClaudeCli"
src/RagBook.Infrastructure/SharedContext/Providers/Cli/
├── ProcessCliRunner.cs                                       # System.Diagnostics.Process impl
├── ClaudeCliAnswerGenerator.cs                               # builds args + stdin, runs, yields stdout (Unavailable on failure)
└── RoutingAnswerGenerator.cs                                 # key? Anthropic : (cli enabled? Cli : throw)
src/RagBook.Infrastructure/DependencyInjection.cs             # keyed IAnswerGenerator (anthropic/cli) + Router default; ICliRunner
src/RagBook.API/
├── Endpoints/ChatEndpoints.cs                                # relax key guards when ClaudeCli:Enabled
├── Endpoints/ConfigEndpoints.cs                              # GET /api/config { keylessGeneration }
└── Program.cs                                                # Configure<ClaudeCliOptions>; MapConfigEndpoints
src/Web/src/app/
├── core/app-config.store.ts                                 # keylessGeneration signal (GET /api/config)
└── chat/chat.ts                                             # locked also unlocks when keylessGeneration
```

## Notes

- Router uses keyed DI (`[FromKeyedServices("anthropic"|"cli")]`) so both generators are injectable + fakeable in the
  router unit test.
- `keylessGeneration` = `ClaudeCli:Enabled || AnthropicOptions.ApplicationKey present` (either backend can serve a
  keyless ask).
- The CLI is not truly token-streaming in text mode — the answer is yielded as one delta (documented; a stream-json
  parser is future work). SSE plumbing is unchanged.
