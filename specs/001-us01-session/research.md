# Phase 0 Research — US-01 Session Isolation + Foundation

No `NEEDS CLARIFICATION` markers remain from the spec; the decisions below resolve every
implementation-level unknown noted in the Clarifications section.

## D1 — Session issuance point

- **Decision**: A first-in-pipeline **ASP.NET Core middleware** (`SessionMiddleware`, registered
  before endpoint routing) reads the session cookie, validates it as a GUID, and — if missing,
  malformed, or unparseable — mints a new `Guid.NewGuid()` (GUID v4). It publishes the resolved id
  into a scoped `ISessionContext` and, after the response is prepared, (re)writes the cookie with a
  fresh 30-day expiry.
- **Rationale**: AC-1 requires *any* endpoint (SPA route or API) to issue a session; middleware is
  the single choke point that runs ahead of all handlers, satisfying "issued on any endpoint" and
  keeping handlers free of session-plumbing.
- **Alternatives**: A Wolverine middleware/behavior (rejected — runs only for dispatched
  messages, not static/SPA routes); per-endpoint code (rejected — not architecturally enforced).

## D2 — Cookie attributes (config-driven)

- **Decision**: `SessionCookieOptions { CookieName = "ragbook_session", SlidingExpiration = 30.00:00:00,
  Secure = true, SameSite = Strict, HttpOnly = true }` bound from `Session:*` configuration.
  Cookie is written with `HttpOnly`, `Secure`, `SameSite=Strict`, `Expires = now + SlidingExpiration`
  (via `TimeProvider`), `Path=/`. `Secure` stays `true`; Aspire serves the API over HTTPS locally,
  so no dev relaxation is required (a config flag exists if ever needed).
- **Rationale**: Exactly matches US-01 constraints; keeps the 30-day window and all flags out of code
  (no magic numbers) per README "Decyzje przekrojowe".
- **Alternatives**: Hard-coded `TimeSpan.FromDays(30)` (rejected — magic number); `SameSite=Lax`
  (rejected — story mandates `Strict`).

## D3 — GUID version

- **Decision**: `Guid.NewGuid()` — produces a random **version-4** GUID, exactly as the story
  requires. Forged/malformed cookie values fail `Guid.TryParse` → treated as a new empty session.
- **Rationale**: Story says GUID v4; `Guid.NewGuid()` is the v4 generator. (`Guid.CreateVersion7`
  is deliberately *not* used — the story specifies v4.)

## D4 — EF Core global query filter (the architectural enforcement)

- **Decision**: `RagBookDbContext` receives `ISessionContext` via constructor injection. In
  `OnModelCreating`, for **every** entity type implementing the `ISessionOwned` marker, apply
  `builder.Entity<T>().HasQueryFilter(e => e.UserSessionId == _session.UserSessionId)` (registered
  generically by reflecting over `ISessionOwned` implementers). Because the DbContext is scoped and
  captures the current `ISessionContext`, every LINQ query is automatically constrained to the
  active session; a handler that "forgets" to filter still cannot see other sessions' rows.
- **Rationale**: This is AC-4 — the filter is enforced by the shared mechanism, not per handler.
  GetById returning `null` for another session's row yields `session.resource_not_found` → **404**,
  satisfying AC-3 (never 403). Applying it by marker interface means future entities opt in by
  implementing `ISessionOwned`, with no per-entity wiring.
- **Alternatives**: A repository base class that appends `.Where(session)` (rejected — bypassable by
  any handler that queries the DbContext directly); manual filters in each handler (rejected — the
  exact footgun AC-4 forbids).
- **Enforcement test (AC-4)**: an integration test seeds a row under session A, then queries the
  `SessionResource` set under session B **without any explicit `.Where`**, asserting zero rows; and
  a companion assertion that `IgnoreQueryFilters()` *would* be required to see it — proving the
  filter is on by default.

## D5 — Session stamping on insert

- **Decision**: A `SaveChangesInterceptor` (`SessionStampingInterceptor`) sets `UserSessionId` on
  every `Added` `ISessionOwned` entity from `ISessionContext` — handlers never set it by hand. A
  sibling `AuditingInterceptor` stamps `IAuditable` (`CreatedAt/By`, `ModifiedAt/By`) using
  `TimeProvider` and the session id (`"system"`-equivalent = `Guid.Empty` for background work).
- **Rationale**: Keeps §VI's "stamped centrally, never by hand" rule and guarantees a created row is
  immediately visible to its own session and invisible to others.

## D6 — Reference session-owned resource (`SessionResource`)

- **Decision**: Introduce a minimal aggregate `SessionResource { Id: Guid, Name: string,
  UserSessionId: Guid, + IAuditable }` in the `Session` module, with Create/GetById/List feature
  slices and `/api/resources` endpoints. It is the isolation test subject and the copy-me template
  for future modules. Labeled in code/README as a foundation reference resource.
- **Rationale**: The product domains (Document/Folder/Conversation) are out of US-01 scope but the
  ACs need a real, filtered, persisted resource exercised through the real host. See plan Complexity
  Tracking for the keep-vs-remove approval point.

## D7 — Dispatch, Result, and error mapping

- **Decision**: Wolverine dispatches `ICommand`/`IQuery`; handlers return `Result<T>`/`Result`.
  `SessionErrors.ResourceNotFound = Error("session.resource_not_found", …, ErrorType.NotFound)`.
  `SessionExceptionHandler` maps Postgres `23505`/FK/concurrency to session codes; a global
  `IExceptionHandler` writes RFC 9457 ProblemDetails with `code`, mapping `NotFound→404`,
  `Validation→400`, `Conflict→409`, `Unauthorized→401/403`, unknown→sanitized 500 `error.unexpected`
  + trace id.
- **Rationale**: §II — body or known code, never a naked 500; frontend branches on `code`.

## D8 — Aspire + Angular wiring

- **Decision**: `RagBook.AppHost` provisions a PostgreSQL resource (pgvector-capable image) and
  references the API; the API references `ServiceDefaults` (`AddServiceDefaults()`). Angular `Web/`
  is added as an Aspire-managed npm resource (dev server). Angular ships a standalone shell with a
  functional `notFoundInterceptor` mapping 404 → a "resource does not exist" signal, and
  `tokens.scss` importing `DESIGN.md` colours as CSS variables (no inline hex).
- **Rationale**: §VIII/§IX — one orchestrator, shared defaults, tokenised UI, backend-managed cookie.
- **Migrations**: created in `RagBook.Infrastructure.Migrations`; applied via `dotnet ef migrations
  bundle` / init step — **never at startup** (§VIII). Integration tests apply the schema against the
  Testcontainers database in fixture setup.
