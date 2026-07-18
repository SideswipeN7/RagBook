# Phase 1 Data Model — US-03

No new entities, columns, or migration. Demo documents are ordinary `documents` (+ `chunks`) rows with
`Origin = Demo`, seeded under a fixed `DemoSessionId`.

## Reused: Document / Chunk (existing)

| Aspect | Demo specialisation |
|---|---|
| `Document.Origin` | `Demo` (existing enum value). Excluded from quota (US-05); read-only for move/delete/bulk (US-10/11/12). |
| `Document.UserSessionId` | `DemoConstants.DemoSessionId` (fixed sentinel) — stamped by the interceptor during the seeder scope. |
| `Document.Id` | Fixed per `DemoOptions.Documents[].Id` (idempotency key). |
| `Chunk` (US-06) | Produced by the normal processing pipeline during seeding; searched by the demo retrieval branch. |

## New domain types (Modules/Demo)

| Type | Shape / role |
|---|---|
| `DemoConstants` | `static readonly Guid DemoSessionId` — the fixed owner for seeded demo rows. |
| `DemoOptions` | `MaxQuestionsPerSession=10`, `MaxQuestionsPerIpPerHour=20`, `SessionCounterTtlHours`, `Documents: DemoDocument[]` (`Id`, `FileName`, `ContentType`, asset path/inline text) — `SectionName "Demo"`. |
| `IDemoDocumentSeeder` | `Task SeedAsync(CancellationToken)` — idempotent by fixed id. |
| `IDemoQuestionCounter` | `int Remaining()`, `int Asked()`, `bool TryConsume()` — per current session, lifetime. |
| `IDemoIpThrottle` | `(bool Allowed, int RetryAfterSeconds) TryRegister(string ip)` — hourly window. |
| `DemoErrors` | `LimitReached` (`chat.demo_limit_reached`, RateLimited → 429), `Unavailable` (`chat.demo_unavailable`, Unavailable → 503). |

## Chat additions

| Type | Change |
|---|---|
| `ChatScopeType` | `+ Demo = 3` |
| `ChatScope` | `+ Demo()` factory (no target) |
| `GroundedContext` | `+ bool IsDemo = false` (key-source flag; set by `AskQuestionPipeline` when scope is Demo) |

## Provider additions

| Type | Change |
|---|---|
| `AnthropicOptions` | `+ string? ApplicationKey` (config/secret, may be null in dev) |
| `IAnthropicClientFactory` | `+ Result<AnthropicClientHandle> CreateForDemo()` (app key, else `DemoErrors.Unavailable`) |
| `AnthropicAnswerGenerator` | picks `context.IsDemo ? CreateForDemo() : CreateForSession()` |

## Read-path changes

| Path | Change |
|---|---|
| `ScopedRetriever` | Demo branch: `WHERE d.origin = @demoOrigin AND d.status = @ready` (no session predicate). |
| `TreeReader` / `TreeData` / `TreeResponse` | `+ DemoDocuments` — a global read (`IgnoreQueryFilters`, `Origin == Demo`, AsNoTracking). |

## Config

| Option | Default | Section |
|---|---|---|
| `DemoOptions.MaxQuestionsPerSession` | 10 | `Demo` |
| `DemoOptions.MaxQuestionsPerIpPerHour` | 20 | `Demo` |
| `DemoOptions.SessionCounterTtlHours` | 24 | `Demo` |
| `DemoOptions.Documents` | 2–3 manifest entries | `Demo` |
| `AnthropicOptions.ApplicationKey` | (secret/env, may be null) | `Anthropic` |

## Frontend state

- `tree.store.ts`: `+ demoDocuments` signal (from `GET /api/tree`).
- `demo.store.ts`: `remaining` / `max` / `asked` / `available` signals (from `GET /api/demo/status`); decrement on a
  successful demo ask; `isExhausted` computed → drives the BYOK nudge.
