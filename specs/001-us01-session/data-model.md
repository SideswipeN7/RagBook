# Phase 1 Data Model — US-01

## Marker & abstractions (Core `Shared/`)

```text
ISessionOwned      { Guid UserSessionId }          # every session-scoped entity implements this
ISessionContext    { Guid UserSessionId }          # ambient current-session accessor (scoped)
IAuditable         { DateTimeOffset CreatedAt; string CreatedBy;
                     DateTimeOffset? ModifiedAt; string? ModifiedBy }
```

- The EF global query filter is applied to **every** `ISessionOwned` type — this is the single
  isolation mechanism (AC-4). New entities become isolated simply by implementing `ISessionOwned`.

## Aggregate: `SessionResource` (Session module, reference resource)

| Field | Type | Rules |
|---|---|---|
| `Id` | `Guid` (PK) | GUID v4, generated on create |
| `Name` | `string` | required, non-empty, trimmed, max length from config/validator |
| `UserSessionId` | `uuid NOT NULL` (indexed) | stamped centrally by `SessionStampingInterceptor`; never set in handlers |
| `CreatedAt/CreatedBy/ModifiedAt/ModifiedBy` | audit | stamped by `AuditingInterceptor` via `TimeProvider` |

- Implements `ISessionOwned` + `IAuditable`.
- **Index**: `IX_session_resources_user_session_id` on `UserSessionId` (every main table gets one).
- **Invariants** (domain-tested): `Name` required; `Create` returns a valid aggregate; identity is a
  fresh GUID. No cross-session behavior lives in the aggregate — isolation is enforced at the query
  boundary, not by the entity.

## Configuration: `SessionCookieOptions` (bound from `Session:*`)

| Key | Default | Meaning |
|---|---|---|
| `Session:CookieName` | `ragbook_session` | cookie name |
| `Session:SlidingExpirationDays` | `30` | validity window; refreshed each visit |
| `Session:Secure` | `true` | `Secure` cookie flag |
| `Session:SameSite` | `Strict` | `SameSite` mode |

- No magic numbers in code — the 30-day window and flags come from config (README constraint).

## State / lifecycle

- **Session**: created on first request without a valid cookie → lives in the cookie only →
  refreshed (expiry slid) on every request → replaced by a new session if the cookie is
  missing/expired/forged. No server-side session record in US-01.
- **SessionResource**: `Created` (stamped with current session) → visible only to that session →
  (delete/update belong to later stories; out of US-01 scope).

## Persistence notes

- `RagBookDbContext` holds `DbSet<SessionResource>`; `OnModelCreating` applies the `ISessionOwned`
  global filter generically and the per-entity index/config.
- Migration `InitialSession` creates `session_resources` with `user_session_id uuid not null` + index.
- Migrations are applied out-of-band (bundle/init/fixture) — never at app startup (§VIII).
