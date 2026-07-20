# Tasks: Keyless generation via the local Claude CLI (US-22)

**Tests**: REQUIRED (§IV). All 4 tiers green before the PR.

- [X] T001 `ClaudeCliOptions` (`Enabled=false`, `Command="claude"`, `Model?`, `TimeoutSeconds=120`; section `ClaudeCli`) in `src/RagBook/Modules/Chat/ClaudeCliOptions.cs`; bind in `Program.cs`.
- [X] T002 `ICliRunner` seam (`RunAsync(command, args, stdin, ct) → (int ExitCode, string StdOut, string StdErr)`) in `src/RagBook/Modules/Chat/Domain/ICliRunner.cs`.
- [X] T003 `ProcessCliRunner` (System.Diagnostics.Process; redirect stdin/stdout/stderr; timeout; kill on cancel) in `src/RagBook.Infrastructure/SharedContext/Providers/Cli/`.
- [X] T004 [P] Application/unit test `ClaudeCliAnswerGeneratorTests` (fake `ICliRunner`): exit 0 → yields stdout; builds args incl. `--append-system-prompt` + `--model` when set; non-zero exit / empty → `AnswerGenerationException(Unavailable)`. (FAIL first.)
- [X] T005 `ClaudeCliAnswerGenerator : IAnswerGenerator` — args `[-p, --output-format, text, (--model, m), (--append-system-prompt, system)]`, stdin = `context.UserPrompt`; yields stdout on success; Unavailable on failure.
- [X] T006 [P] Application/unit test `RoutingAnswerGeneratorTests` (fake generators + fake `IAnthropicClientFactory` + options): key present → anthropic; no key + `Enabled` → cli; no key + disabled → `AnswerGenerationException` (InvalidKey non-demo / Unavailable demo). (FAIL first.)
- [X] T007 `RoutingAnswerGenerator : IAnswerGenerator` (keyed `anthropic`/`cli` generators + `IAnthropicClientFactory` + `IOptions<ClaudeCliOptions>`); DI: keyed `AnthropicAnswerGenerator`("anthropic") + `ClaudeCliAnswerGenerator`("cli") + `ICliRunner` + `RoutingAnswerGenerator` as the default `IAnswerGenerator`.
- [X] T008 `ChatEndpoints`: relax the key guards — `CreateForSession().IsFailure && !cliEnabled → 401`; demo `CreateForDemo().IsFailure && !cliEnabled → 503` (inject `IOptions<ClaudeCliOptions>`).
- [X] T009 `GET /api/config` → `{ keylessGeneration }` (= `ClaudeCli:Enabled` only — the app key serves the demo scope, already unlocked; counting it would unlock non-demo scopes the guard still 401s) in `src/RagBook.API/Endpoints/ConfigEndpoints.cs`; `MapConfigEndpoints` in `Program.cs`. Integration test: with `ClaudeCli:Enabled=true` → `keylessGeneration:true` + a keyless ask is not 401.
- [X] T010 Frontend `core/app-config.store.ts` (`keylessGeneration` from `GET /api/config`) + `chat.ts` `locked` also unlocks when `keylessGeneration()`; App/Chat refresh config. Karma.
- [X] T011 README/`.env.example`: document `ClaudeCli__Enabled` (local/self-host; CLI must be installed + authenticated on the API host; not in the default container). Run all 4 tiers green; critical review; PR.

## Notes

- CLI generation is a keyless **fallback**; a present key always wins. Off by default. Prompt passed via arg list +
  stdin (no shell string). CLI failure → provider-unavailable (no crash).
