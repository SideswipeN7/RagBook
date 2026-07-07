# Quickstart — Validate US-01

## Prerequisites

- .NET 10 SDK, Node.js (Angular), Docker running (for integration tests / Aspire PostgreSQL).

## Run locally (Aspire)

```sh
cd src/Web && npm install && cd -        # install SPA deps once (prerequisite for the web resource)
dotnet run --project src/RagBook.AppHost
# Aspire dashboard prints its URL; it starts PostgreSQL, the API, and the Angular dev server
# (the SPA is orchestrated via AddExecutable running `npm run start`).
```

## Automated validation (the source of truth for DoD)

```sh
# Cheapest tiers first (no Docker)
dotnet test tests/RagBook.Domain.Tests
dotnet test tests/RagBook.Application.Tests

# Integration tier — START DOCKER FIRST (Testcontainers PostgreSQL)
dotnet test tests/RagBook.Api.IntegrationTests
```

Integration tests map to acceptance criteria:

| AC | Test (`Should_..._When_...`) | Proves |
|---|---|---|
| AC-1 | `Should_IssueSessionCookie_When_RequestHasNoCookie` | new GUID v4 cookie set, `HttpOnly`/`Secure`/`SameSite=Strict`/~30d; empty-session state returned |
| AC-2 | `Should_ReturnOwnResourcesAndRefreshCookie_When_ReturningWithValidCookie` | same cookie → own data visible; expiry refreshed |
| AC-2 (edge) | `Should_StartEmptySession_When_CookieIsForgedOrExpired` | forged/expired cookie → new empty session, no error |
| AC-3 | `Should_Return404_When_RequestingAnotherSessionsResourceById` | B reads A's id → 404 (never 403) |
| AC-3 | `Should_NotListAnotherSessionsResources_When_Listing` | A's resource absent from B's list |
| AC-4 | `Should_ExcludeOtherSessionRows_When_QueryingWithoutExplicitFilter` | global query filter enforced by the shared mechanism; `IgnoreQueryFilters()` required to bypass |

## Manual smoke (optional)

```sh
# First visit issues a cookie
curl -i http://localhost:<api>/api/session            # → 200 {"isNew":true,...} + Set-Cookie
# Create a resource, then read it back with the same cookie jar
curl -c jar -b jar -X POST http://localhost:<api>/api/resources -d '{"name":"hello"}' -H 'Content-Type: application/json'
curl -c jar -b jar http://localhost:<api>/api/resources/<id>     # → 200
# A different (empty) cookie jar cannot see it
curl http://localhost:<api>/api/resources/<id>                   # → 404 session.resource_not_found
```

## Expected outcomes

- Every response sets/refreshes the session cookie with the mandated flags.
- A session sees only its own resources; another session's resource by id is `404`, never `403`.
- Removing the global query filter makes AC-3/AC-4 tests fail — the guardrail is real.
