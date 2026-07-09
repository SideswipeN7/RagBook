# Phase 0 Research — US-05 File Quota

No `NEEDS CLARIFICATION` markers remain in the spec. The one open item — how much of `Document` to
build now — is a scope decision escalated to the captain (spec Clarifications); this plan proceeds on
"build a minimal persisted `Document`". The decisions below resolve every implementation-level unknown.

## D1 — Minimal `Document` shape (and why persist it now)

- **Decision**: Introduce a minimal, persisted `Document` aggregate in a new `Documents` module,
  implementing `ISessionOwned` + `IAuditable`, carrying only what the quota needs:
  `Id: Guid`, `UserSessionId: Guid` (stamped centrally), `SizeBytes: long`, `Status: DocumentStatus`
  (`Processing | Ready | Failed`), `Origin: DocumentOrigin` (`User | Demo`). A migration `AddDocuments`
  creates a `documents` table with an index on `user_session_id`.
- **Rationale**: **AC-5** requires the quota check and the insert to be atomic and proves it against a
  real database under concurrency — impossible without a real table and insert path. The minimal shape
  is exactly the columns the quota reads; **US-04 extends the same aggregate/table** with filename,
  storage pointer, and richer processing states (US-06). `Status` includes `Failed` now because
  `Failed` documents count toward the quota (story decision).
- **Alternatives**: (a) a pure in-memory/abstract count seam with no table — rejected: cannot satisfy
  AC-5's atomic-insert concurrency proof, and gives the quota nothing real to read. (b) Building the
  full US-04 upload aggregate now — rejected: out of scope, drags in filenames/content/storage.

## D2 — Count/size seam (`Failed` counts, `Demo` excluded)

- **Decision**: `IDocumentQuotaRepository` exposes `CountAsync(ct)` and `SumSizeBytesAsync(ct)` that
  count/sum the current session's documents **excluding `Origin == Demo`**, i.e. `Where(d => d.Origin
  != DocumentOrigin.Demo)`. `Status` is not filtered, so `Failed` documents are included. Isolation to
  the current session is inherited from the existing global query filter — the seam adds no session
  predicate of its own.
- **Rationale**: FR-007 — `Failed` counts until deleted; demo documents (US-03) never count. The
  `Origin` discriminator is the forward-looking seam: US-03 will create `Demo` documents and they are
  excluded automatically, with no change to this repository. In US-05 only `User` documents exist.
- **Alternatives**: A `bool CountsTowardQuota` flag on `Document` — rejected: an `Origin` enum is more
  expressive for later stories (demo vs. user vs. future imported) and avoids a second boolean to keep
  in sync with origin.

## D3 — Atomic admit under concurrency (AC-5) — **transaction-scoped advisory lock**

- **Decision**: `IDocumentQuotaRepository.TryAddWithinQuotaAsync(Document doc, QuotaLimits limits, ct)`
  runs inside a single DB transaction that first takes a **PostgreSQL transaction-scoped advisory
  lock keyed by the session id** (`SELECT pg_advisory_xact_lock(<key>)`, where `<key>` is a stable
  `bigint` derived from the session GUID). Under the lock it re-reads the count and size sum
  (excluding demo), evaluates `QuotaSnapshot.CanAdmit(doc.SizeBytes)`, and either `Add` + `SaveChanges`
  + commit, or returns `quota.exceeded` / `quota.total_size_exceeded` without inserting. The lock is
  released automatically at commit/rollback.
- **Rationale**: Two concurrent admits for the same session serialize on the advisory lock: the first
  inserts the 10th document and commits; the second then re-counts 10 and returns `quota.exceeded`. At
  most one wins — exactly AC-5 — **without a retry loop**, and **different sessions never contend**
  (distinct keys). The re-read inside the lock is what makes the check+insert atomic.
- **Key derivation**: `unchecked((long)BitConverter.ToInt64(sessionId.ToByteArray(), 0))` — a stable,
  deterministic 64-bit key per session (collision across sessions only parks unrelated uploads briefly;
  correctness is unaffected because the in-lock re-count is always session-filtered).
- **Alternatives**:
  - **`Serializable` isolation** — correct, but forces a **retry loop** on `40001` serialization
    failures and escalates locking; more moving parts for the same guarantee. Rejected as heavier.
  - **Unique constraint on `(user_session_id, slot)`** with an allocated slot 1..N — turns over-quota
    into a `23505`, but requires allocating/reclaiming slot numbers (fragile with deletes) and only
    caps the count, not the byte total. Rejected as complex and count-only.
  - **Application-level lock (`SemaphoreSlim`)** — rejected: does not hold across the stateless,
    horizontally-scaled API replicas the constitution mandates (§VIII).

## D4 — MB convention

- **Decision**: Storage is stored and compared in **bytes** (`long`). Limits `MaxFileSizeMb` /
  `MaxTotalMb` are megabytes in config, converted to bytes as **decimal MB = 1,000,000 bytes**
  (`MaxTotalBytes = MaxTotalMb * 1_000_000L`). The UI shows `UsedMb` = `round(UsedBytes / 1_000_000, 1)`.
- **Rationale**: One consistent, documented convention avoids the 50 MB-vs-52.4 MB ambiguity; decimal
  MB matches how end users read "50 MB". All arithmetic is in bytes so rounding never affects
  enforcement — only display.
- **Alternatives**: Binary MiB (1,048,576) — rejected: less intuitive for the "X / 50 MB" counter and
  not what the story's numbers imply.

## D5 — `QuotaOptions` (config-driven, zero magic numbers)

- **Decision**: `QuotaOptions { int MaxDocuments = 10; int MaxFileSizeMb = 10; int MaxTotalMb = 50 }`
  bound from the `Quota` configuration section via `IOptions<QuotaOptions>` (registered in
  `Program.cs`). `QuotaService` reads it and projects a `QuotaLimits` value object (bytes). Defaults
  live on the options type; `appsettings.json` carries an explicit `Quota` section for demonstrability.
- **Rationale**: §VII names `QuotaOptions` as config-driven with zero magic numbers; "quota-ready"
  means raising a tier is a config edit, not a code change. `QuotaService` never sees literals.
- **Edge case (FR-009)**: lowering a limit below current usage never deletes rows — enforcement is
  purely "can we admit the next upload"; existing documents are untouched, new admits stay blocked
  until `Used < Max` again. This falls out of the design for free (no reconciliation logic).

## D6 — Quota arithmetic as a pure domain value object

- **Decision**: `QuotaSnapshot { int UsedDocuments; long UsedBytes; QuotaLimits Limits }` exposes
  `Result CanAdmit(long fileSizeBytes)` returning `quota.file_too_large` (file > `MaxFileSizeBytes`),
  `quota.exceeded` (`UsedDocuments >= MaxDocuments`), `quota.total_size_exceeded`
  (`UsedBytes + fileSizeBytes > MaxTotalBytes`), or success; plus `UsedMb`, `RemainingBytes`,
  `IsFull`, `CanUpload`. The same `CanAdmit` is used by `CheckCanUpload` (pre-check) and inside the
  advisory-lock admit (authoritative check).
- **Rationale**: Centralises every boundary rule in one pure, domain-tested type — AC-2 (at 10 →
  exceeded), AC-3 (45+8 > 50 → total-size), exactly-at-limit admitted, file-too-large — all become
  fast Domain-tier tests, and the atomic path can't drift from the pre-check because they share it.
- **Note**: `quota.file_too_large` guards `MaxFileSizeMb`; no AC exercises it directly, but the option
  exists and the upload (US-04) relies on the guard — cheap to include, keeps the option meaningful.

## D7 — `GET /api/quota` and the UI refresh model

- **Decision**: `GetQuotaQuery : IQuery<QuotaStateResponse>` → `GetQuotaQueryHandler` calls
  `IQuotaService.GetStateAsync`. `QuotaEndpoints` maps `GET /api/quota` returning `usedDocuments`,
  `maxDocuments`, `usedMb`, `maxTotalMb`, `maxFileSizeMb`, and `canUpload`. The Angular `QuotaStore`
  (signals, `providedIn: 'root'`) fetches it and exposes `state`, `canUpload`, `isFull`; upload
  (US-04) and delete (US-08) call `QuotaStore.refresh()` so the counter updates without a page reload
  (AC-4). The `quota-bar` component renders the two meters and, when full, the "delete files" hint.
- **Rationale**: §IX — backend-managed state reflected by a signal store; the shared store is the
  "wspólny store/sygnał" the story requires for post-upload/-delete refresh.

## D8 — Module exception handler & error catalog

- **Decision**: `QuotaErrors` (module catalog): `quota.exceeded` (Conflict), `quota.total_size_exceeded`
  (Conflict), `quota.file_too_large` (Validation), `quota.conflict` (Conflict, infra fallback).
  `DocumentsExceptionHandler.TryMap` translates a persistence `UniqueViolation`/`ConcurrencyConflict`
  to `quota.conflict`, mirroring `SessionExceptionHandler`; unknown faults fall through to the global
  mapper. With the advisory lock, over-quota is returned as a `Result` (not an exception), so the
  handler is a safety net, not the primary path.
- **Rationale**: §II — each module owns a closed catalog + an infra→code handler; over-quota is an
  expected failure returned via `Result`, never thrown.

## Testing strategy (Red → Green → Refactor, cheapest tier first)

| AC | Tier | Test (`Should_..._When_...`) |
|---|---|---|
| AC-2 (count) | Domain | `Should_ReturnQuotaExceeded_When_DocumentCountAtLimit` |
| AC-3 (size) | Domain | `Should_ReturnTotalSizeExceeded_When_AddingFileWouldCrossTotal` |
| boundary | Domain | `Should_Admit_When_UsageExactlyAtLimitBoundary`; `Should_ReturnFileTooLarge_When_FileExceedsPerFileMax` |
| — | Domain | `Should_CreateProcessingUserDocument_When_CreatedForQuota` (Document invariants) |
| AC-1 | Application | `Should_ReturnUsedCountAndMb_When_GettingQuotaState` (mocked repo) |
| AC-2/3 | Application | `Should_FailFast_When_CheckCanUploadOverCount/Size` (QuotaService, mocked repo) |
| AC-1 | Integration | `Should_ReportCountAndUsage_When_SessionHasDocuments` (Testcontainers, real COUNT/SUM) |
| FR-007 | Integration | `Should_ExcludeDemoDocuments_When_CountingQuota` |
| **AC-5** | Integration | `Should_AdmitAtMostOneDocument_When_TwoUploadsRaceAtLimit` (two concurrent admits at 9/10 → count == 10, exactly one success) |

Front-end: a `quota-bar` unit test asserts it renders "X / 10 plików" and the MB read-out from a
stubbed `QuotaStore`, and shows the full-state hint when `isFull`.
