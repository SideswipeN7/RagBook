# Feature Specification: User Session (Data Isolation)

**Feature Branch**: `fm/us01-session`

**Created**: 2026-07-07

**Status**: Draft

**Input**: US-01 — Sesja użytkownika (izolacja danych). Anonymous per-visitor session
identifying a user without login, so their documents, folders and conversations are fully
isolated from other users. Foundation story (P1, blocks everything). Binding cross-cutting
decisions from `docs/features/README.md` apply.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Anonymous session on first visit (Priority: P1)

A first-time visitor arrives with no session cookie. The system silently issues them an
anonymous identity and returns the empty-session application state, without any login,
registration, or prompt. From this moment every resource they create is tied to that
identity.

**Why this priority**: This is the foundation the entire product stands on — no document,
folder, or conversation feature can exist without an owning identity. It blocks all other
stories.

**Independent Test**: Issue a request with no session cookie to any SPA or API endpoint;
observe that the response sets a session cookie and returns valid empty-session state.

**Acceptance Scenarios**:

1. **Given** a new visitor with no session cookie, **When** they open the application
   (any SPA route or API endpoint), **Then** the backend generates a `UserSessionId`
   (GUID v4), sets a session cookie, and returns application state for an empty session.
2. **Given** the response from the first request, **When** the cookie is inspected,
   **Then** it is marked `HttpOnly`, `Secure`, `SameSite=Strict`, and carries a 30-day expiry.

---

### User Story 2 - Returning user sees their data, refreshed session (Priority: P1)

A returning visitor who still holds a valid session cookie is recognised as the same
identity, sees the documents and folders they created before, and has their session validity
extended so it does not lapse while they keep using the app.

**Why this priority**: Persistence is what makes the anonymous session useful — without it,
every visit would strand the user's prior work. It is inseparable from the isolation guarantee.

**Independent Test**: Create data under a session cookie, then replay a request with the same
cookie after simulated browser restart; observe the same data returned and a refreshed cookie
expiry.

**Acceptance Scenarios**:

1. **Given** a visitor with a valid session cookie who created documents/folders earlier,
   **When** they return after closing the browser, **Then** they see their own documents and
   folders and the cookie's expiry is refreshed to 30 days from now.
2. **Given** a returning visitor, **When** the session cookie is missing, forged, or expired,
   **Then** the system treats them as a fresh empty session (no data, no error) and issues a
   new session.

---

### User Story 3 - Cross-session resource isolation (Priority: P1)

Two independent visitors (sessions A and B) each have their own resources. Neither can see
nor reach the other's resources: listing shows only one's own, and requesting the other's
resource by its identifier is indistinguishable from that resource not existing at all.

**Why this priority**: Isolation is the core promise of the story — a leak between anonymous
sessions would be a privacy failure. Enforced architecturally so no future feature can
accidentally break it.

**Independent Test**: Create a resource under session A, then request it (and list resources)
under session B; observe a not-found result and that A's resource never appears in B's lists.

**Acceptance Scenarios**:

1. **Given** sessions A and B and a resource (document/folder/conversation) owned by A,
   **When** B requests that resource by its identifier, **Then** the API responds
   **404 Not Found** (never 403), disclosing nothing about the resource's existence.
2. **Given** sessions A and B, **When** B lists resources, **Then** A's resources never
   appear in any of B's lists.
3. **Given** any handler that reads domain data, **When** it queries the datastore, **Then**
   the query is constrained to the current session's identity by a shared, architecturally
   enforced mechanism (not per-handler hand-written filters), and an integration test verifies
   this constraint holds.

---

### Edge Cases

- **Expired or deleted cookie** → a new empty session is issued; the previous session's data
  becomes orphaned (cleanup is out of scope for MVP; recorded as a known limitation).
- **Manually forged / malformed GUID in cookie** → treated as an empty session (no data
  returned), never surfaced as an error.
- **Concurrent first requests from one new visitor** → a single consistent session identity
  should result (no duplicate/torn identity); the cookie set on the first response is honoured.
- **Request to another session's resource that also does not exist** → same 404 as an existing
  resource owned by another session (responses must be indistinguishable).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST issue an anonymous `UserSessionId` (GUID v4) to any visitor who
  arrives without a valid session cookie, on any SPA route or API endpoint, with no login or
  registration step.
- **FR-002**: The system MUST persist the `UserSessionId` in a cookie that is `HttpOnly`,
  `Secure`, `SameSite=Strict`, with a 30-day validity that is refreshed (slid forward) on every
  visit.
- **FR-003**: The system MUST recognise a returning visitor by their valid session cookie and
  scope all data they see to that `UserSessionId`.
- **FR-004**: The system MUST treat a missing, expired, or forged/malformed session cookie as a
  fresh empty session — issuing a new identity, returning no data, and never returning an error
  for the invalid cookie.
- **FR-005**: Every domain entity that stores user-owned data (Document, Folder, Conversation,
  and any future domain entity) MUST carry a non-nullable `UserSessionId` and MUST be indexed by
  it.
- **FR-006**: The system MUST constrain every read of domain data to the current session's
  `UserSessionId` through a single shared, architecturally enforced mechanism, so that no
  individual handler can accidentally issue an unfiltered query.
- **FR-007**: The system MUST respond with **404 Not Found** — never 403 — when a session
  requests a resource it does not own, so that resource existence is not disclosed.
- **FR-008**: The system MUST exclude resources not owned by the current session from every list
  or collection response.
- **FR-009**: The current session identity MUST be available to application handlers via an
  injected `ISessionContext` exposing the `UserSessionId`.
- **FR-010**: The frontend MUST NOT implement isolation logic; it MUST rely on the
  backend-managed cookie and map a 404 response to a "resource does not exist" experience.
- **FR-011**: Cookie validity and related tunables MUST be configuration-driven (no magic
  numbers in code).

### Key Entities *(include if feature involves data)*

- **User Session**: An anonymous identity for one visitor, keyed by `UserSessionId` (GUID v4).
  Not a login account; created on first visit, carried in a cookie, refreshed on use. Owns all
  of the visitor's domain data.
- **Session-owned domain entity**: Any entity representing user-owned data (Document, Folder,
  Conversation, …). Each references exactly one `UserSessionId` and is only ever visible to that
  session.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of requests arriving without a valid session cookie receive a newly issued
  session cookie and a valid empty-session response — no request is rejected for lacking a session.
- **SC-002**: A returning visitor with a valid cookie sees 100% of their own prior resources and
  0% belonging to any other session.
- **SC-003**: Every attempt by one session to read another session's resource by identifier
  returns 404, with 0% returning 403 or leaking existence, across documents, folders, and
  conversations.
- **SC-004**: Every session cookie observed in responses is `HttpOnly`, `Secure`,
  `SameSite=Strict` with an expiry ~30 days out, refreshed on each visit.
- **SC-005**: An automated integration test demonstrates that a handler cannot read data across
  sessions even when it issues a query without an explicit per-handler filter (the shared
  mechanism enforces isolation).

## Assumptions

- No login, registration, session recovery, or GDPR-style cleanup of orphaned data is in scope
  (out of scope for the whole MVP; orphaned-data cleanup recorded as a known limitation).
- No multi-tenant model beyond per-visitor session isolation is in scope.
- The document, folder, and conversation domains referenced for isolation are delivered by later
  stories; US-01 establishes the mechanism and a minimal `Session` module slice, and does not
  build those feature domains.
- The datastore supports enforcing a shared session filter across all domain reads (satisfied by
  the fixed stack: EF Core global query filters over PostgreSQL).
- "Any endpoint issues a session" is satisfied by a request-pipeline mechanism that runs ahead of
  application handlers.
