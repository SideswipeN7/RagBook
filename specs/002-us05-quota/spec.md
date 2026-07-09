# Feature Specification: File Quota (Limit plików)

**Feature Branch**: `fm/us05-quota`

**Created**: 2026-07-07

**Status**: Draft

**Input**: US-05 — Limit plików (quota). A per-session quota that caps how many documents a
visitor may hold and how much total storage they may use, shown as an always-visible counter,
and enforced server-side before any upload is written. Foundation-adjacent story (P1) in
milestone M1; depends only on US-01 (session isolation). Binding cross-cutting decisions from
`docs/features/README.md` ("Decyzje przekrojowe") apply — all limits are configuration-driven
with zero magic numbers.

## Clarifications

### Session 2026-07-07

A structured ambiguity scan (functional scope, domain/data, UX flow, non-functional, integration,
edge cases, constraints, terminology, completion signals) was run against this spec. Most material
decisions are already fixed by US-05 "Kontekst / decyzje projektowe" and the README "Decyzje
przekrojowe" and are therefore not re-opened —

- Limits are configuration-driven (`QuotaOptions`, defaults 10 docs / 10 MB per file / 50 MB total),
  zero magic numbers — fixed by the story + README.
- Enforcement is server-side, before any write; UI only reflects state — fixed by the story.
- `Failed` documents count toward quota; demo documents (US-03) do not — fixed by the story.
- Errors flow through `Result<T>` → ProblemDetails with a stable `code` — fixed by README + constitution §II.
- Cross-session isolation is inherited from US-01 (global query filter), not re-implemented — fixed.

**One open scope-sequencing decision is escalated to the captain** (it is not an implementation
detail — it changes what US-05 builds vs. defers to US-04):

- Q: How much of the `Document` representation should US-05 build now vs. defer to US-04 (upload)?
  → **A (recommended, provisional — pending captain confirmation): build a minimal persisted
  `Document` now.** US-05 introduces a new `Documents` module with a minimal, persisted `Document`
  entity (own table + migration) carrying only what the quota needs — `Id`, `UserSessionId`,
  `SizeBytes`, a lifecycle `Status` (incl. `Failed`), and an `Origin`/demo marker so demo docs can
  later be excluded — plus the atomic "reserve-and-insert" seam and the count/size read seam. US-04
  extends the same `Document`/table with filename, content, and processing. **Rationale**: AC-5 is a
  mandatory concurrency requirement (quota check + insert atomic, proven by a Testcontainers test),
  which is only demonstrable against a real insert path and a real table — so a minimal persisted
  `Document` must exist in US-05. A pure in-memory/abstract seam with no table cannot satisfy AC-5.
  *If the captain prefers US-05 ship only an abstract seam and defer all persistence to US-04, AC-5's
  concurrency proof would have to move to US-04 — flagged as the trade-off.*

Implementation-level decisions (resolved in `plan.md`, not by product input): the concurrency
mechanism for AC-5 (advisory lock vs. `Serializable` transaction vs. a reservation constraint), the
MB convention (decimal MB = 1,000,000 bytes, surfaced in the plan), the exact shape of the count/size
repository seam, and the quota-bar component/store structure.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - See the current quota (Priority: P1)

A visitor with some documents opens the documents view and sees, at a glance, how full their
quota is: how many of their allowed file slots are used and how much of their allowed storage is
consumed. The counter reflects their own session only.

**Why this priority**: Predictable, visible limits are the whole point of the story — the user
must understand the free-tier ceiling before they hit it. The counter is the one artifact every
other acceptance scenario refers to.

**Independent Test**: Seed a session with N documents totalling M megabytes, request the quota
state, and observe a response reporting `N` of the file limit used and `M` of the storage limit
used, alongside the configured limits.

**Acceptance Scenarios**:

1. **Given** a session holding 7 documents, **When** the visitor opens the documents view,
   **Then** they see a counter reading "7 / 10 plików" and a storage usage read-out "X / 50 MB",
   where X is their current total usage.
2. **Given** two independent sessions A and B with different document counts, **When** each reads
   its quota, **Then** each sees only its own count and usage — never the other's.

---

### User Story 2 - Blocked when the file-count quota is full (Priority: P1)

A visitor who already holds the maximum number of documents tries to add another. The system
refuses the upload before writing anything, tells them the quota is full, and suggests freeing
space by deleting files. The upload control is presented as unavailable with an explanatory hint.

**Why this priority**: This is the enforcement guarantee — a limit that can be exceeded is not a
limit. It must hold server-side regardless of what the UI does.

**Independent Test**: Seed a session at the file-count limit, attempt an upload, and observe a
failure carrying the file-count-exceeded code with nothing persisted.

**Acceptance Scenarios**:

1. **Given** a session holding the maximum allowed documents (10), **When** the visitor attempts
   to upload another file, **Then** the system rejects it with a stable "quota exceeded" error
   code **before persisting anything**, and the document count is unchanged.
2. **Given** the rejection, **When** it is surfaced in the UI, **Then** the visitor sees a
   message that the quota is full and a suggestion to delete files, and the upload control is
   shown disabled with an explanatory tooltip.

---

### User Story 3 - Blocked when the total-size quota would be exceeded (Priority: P1)

A visitor with room in their file count but little storage headroom tries to upload a file large
enough to push them over the total-storage limit. The system refuses it and tells them how much
space is actually available.

**Why this priority**: Storage is the second, independent dimension of the quota; a file-count
check alone would let a few large files blow past the storage ceiling.

**Independent Test**: Seed a session with usage close to the storage limit, attempt an upload of
a file whose size would exceed the remaining headroom, and observe the total-size-exceeded
failure with the available headroom reported.

**Acceptance Scenarios**:

1. **Given** a session using 45 MB against a 50 MB storage limit, **When** the visitor uploads an
   8 MB file, **Then** the system rejects it with a stable "total size quota exceeded" error code
   **before persisting anything**, and the response conveys the remaining available space.

---

### User Story 4 - Quota frees up after deletion (Priority: P1)

A visitor at full quota deletes a document. The counter drops immediately and uploading becomes
possible again, without reloading the page.

**Why this priority**: The quota must be a live, two-way reflection of state — the user's own
cleanup is the intended remedy for a full quota, so the loop has to close visibly.

**Independent Test**: With a session at full quota, remove one document, re-read the quota state,
and observe the count and usage decreased and the "can upload" condition restored.

**Acceptance Scenarios**:

1. **Given** a session at full quota, **When** the visitor deletes a document, **Then** the
   counter decreases and a subsequent upload is admitted — and the UI reflects the freed slot
   without a full page reload.

> **Dependency note**: the delete action itself is delivered by US-08 and the upload action by
> US-04. US-05 owns the quota state and the seam both consume; this scenario is validated in
> US-05 by driving the underlying count/size seam directly, and end-to-end once US-04/US-08 land.

---

### User Story 5 - Two concurrent uploads at the boundary (Priority: P1)

A visitor one slot below the file-count limit fires two uploads at the same time (e.g. two
browser tabs, a double-submit). At most one may be admitted; the quota must not be exceeded by a
race between the check and the write.

**Why this priority**: A check-then-write quota is only correct if it is atomic. Without this,
the limit is advisory under concurrency — exactly the failure the story calls out.

**Independent Test**: With a session at one-below-limit, issue two admit-and-insert operations
concurrently against a real database, and assert the resulting document count never exceeds the
limit (at most one of the two succeeds).

**Acceptance Scenarios**:

1. **Given** a session holding 9 of 10 allowed documents, **When** two uploads arrive
   concurrently, **Then** at most one is admitted and the final document count is at most 10 —
   the quota check and the insert are atomic (single transaction with an appropriate isolation
   level, or a database constraint / advisory lock).

---

### Edge Cases

- **Config limit lowered below current usage** → a configuration change that sets a limit below a
  session's current count/usage leaves existing documents untouched; new uploads stay blocked
  until the session drops back below the limit. No documents are deleted by a limit change.
- **`Failed` documents** → documents that failed processing still count toward the quota until the
  user deletes them (a deliberate decision: the user sees the problem and cleans up; the
  alternative is recorded in the README rationale).
- **Demo documents** → documents belonging to the demo mode (US-03) do **not** count toward the
  quota; the count/size seam must be able to exclude them once demo mode exists.
- **Exactly-at-limit upload** → an upload that brings usage to exactly the limit (10th file, or a
  file filling the last byte of storage headroom) is admitted; only crossing the limit is
  rejected.
- **Empty session** → a brand-new session reports zero used against the full configured limits and
  admits uploads normally.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST expose the current session's quota state — number of documents used,
  total storage used, and the configured limits (max documents, max total storage, and max single
  file size) — through a read endpoint scoped to the current session.
- **FR-002**: The system MUST enforce, server-side and before persisting any uploaded file, that
  admitting the file keeps the session at or below both the maximum document count and the maximum
  total storage.
- **FR-003**: When admitting a file would exceed the maximum document count, the system MUST reject
  it with a stable, machine-readable "quota exceeded" error code drawn from the module's error
  catalog, without persisting anything.
- **FR-004**: When admitting a file would exceed the maximum total storage, the system MUST reject
  it with a distinct stable "total size quota exceeded" error code, without persisting anything,
  and the response MUST convey the remaining available space.
- **FR-005**: All quota limits (maximum document count, maximum single-file size, maximum total
  storage) MUST be configuration-driven with no magic numbers in code, and MUST default to 10
  documents, 10 MB per file, and 50 MB total.
- **FR-006**: The quota check MUST count only the current session's documents (isolation is
  inherited from US-01); one session's usage MUST never affect another's quota.
- **FR-007**: `Failed` documents MUST count toward the quota until deleted; demo-mode documents
  (US-03) MUST be excluded from the quota — the counting seam MUST support this distinction even
  though demo mode is not built in this story.
- **FR-008**: The quota check and the document insert MUST be atomic under concurrency, so that two
  concurrent uploads at one-below-limit admit at most one — via a single transaction at an
  appropriate isolation level, or a database constraint / advisory lock. This MUST be proven by a
  concurrency test against a real database.
- **FR-009**: A configuration change lowering a limit below a session's current state MUST NOT
  delete or alter existing documents; it MUST only block new uploads until the session falls back
  below the limit.
- **FR-010**: The frontend MUST display a quota bar/counter showing files used against the file
  limit and storage used against the storage limit, and MUST refresh it after an upload or a
  deletion without a full page reload (via a shared client-side store/signal).
- **FR-011**: When the quota is full, the frontend MUST present the upload control as disabled with
  an explanatory tooltip and MUST surface a message suggesting the user delete files.
- **FR-012**: All quota failures MUST be returned through the standard `Result` → ProblemDetails
  channel with a stable `code`, never as a naked 500 (constitution §II).

### Key Entities *(include if feature involves data)*

- **Quota**: The per-session ceiling and current standing — used document count and used storage
  measured against the configured maximums. Derived from the session's documents, not stored as
  its own row; recomputed on demand.
- **Document (minimal representation)**: A session-owned file record. US-05 needs only the
  attributes the quota reads — its owning session, its size in bytes, and enough of a lifecycle
  marker to distinguish quota-counting documents (including `Failed`) from excluded demo documents.
  The full document aggregate and the upload flow are delivered by US-04, which wires through this
  same counting seam. *(How much of this to build now vs. defer to US-04 is a clarification for
  `/speckit-clarify`.)*
- **Quota Limits (configuration)**: The tunable ceilings — maximum document count, maximum
  single-file size, maximum total storage — bound from configuration, "quota-ready" for future
  paid tiers without code changes.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A session holding N documents totalling M megabytes sees a quota read-out reporting
  exactly N used of the file limit and M used of the storage limit, matching the configured limits
  — for 100% of reads.
- **SC-002**: 100% of upload attempts that would exceed either the file-count or the total-storage
  limit are rejected before anything is persisted, each with its correct distinct error code.
- **SC-003**: Under two concurrent uploads at one-below-limit, the resulting document count never
  exceeds the limit across repeated runs — at most one upload is admitted, 0% over-admit.
- **SC-004**: Changing a configured limit requires no code change and takes effect on the next
  quota evaluation; lowering a limit below current usage deletes 0 existing documents.
- **SC-005**: After a deletion at full quota, the counter reflects the freed slot and a subsequent
  upload is admitted without a page reload, in 100% of cases.
- **SC-006**: The quota of one session is unaffected by any other session's documents in 100% of
  cross-session checks.

## Assumptions

- **US-01 is in place**: session identity, the `UserSessionId` column, the global query filter,
  and central session stamping already exist and are reused unchanged; the quota inherits isolation
  from them rather than re-implementing it.
- **Upload (US-04) and delete (US-08) are not built here**: US-05 delivers the quota mechanism, the
  read endpoint, the configuration, the counting seam, and the quota-bar UI. The upload handler
  that calls the quota check and the delete that frees it arrive in later stories and wire through
  this story's seam. Scenarios that reference upload/delete are validated in US-05 by driving the
  seam directly.
- **Minimal `Document` now**: a minimal document representation sufficient for counting and sizing
  may be introduced in this story so the quota has something real to read; the full aggregate is
  US-04's. The exact extent is a `/speckit-clarify` question.
- **Demo mode (US-03) is not built**: only a forward-looking exclusion seam is provided so demo
  documents can later be excluded from the quota.
- **Storage is measured in bytes internally** and surfaced in megabytes to the user; "MB" in the UI
  uses the project's standard MB convention (documented in the plan).
- **Out of scope**: paid tiers, per-user limit overrides, and any admin panel for managing quotas.
