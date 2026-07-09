# Phase 1 Data Model — US-05

## Aggregate: `Document` (Documents module, minimal shape)

| Field | Type | Rules |
|---|---|---|
| `Id` | `Guid` (PK) | GUID v4, generated on create |
| `UserSessionId` | `uuid NOT NULL` (indexed) | stamped centrally by `SessionStampingInterceptor`; never set in handlers |
| `SizeBytes` | `bigint NOT NULL` | `>= 0`; the file's size in bytes (the quota's storage unit) |
| `Status` | `int NOT NULL` | `DocumentStatus` — `Processing (0) \| Ready (1) \| Failed (2)`; `Failed` still counts toward quota |
| `Origin` | `int NOT NULL` | `DocumentOrigin` — `User (0) \| Demo (1)`; `Demo` is **excluded** from the quota |
| `CreatedAt/CreatedBy/ModifiedAt/ModifiedBy` | audit | stamped by `AuditingInterceptor` via `TimeProvider` |

- Implements `ISessionOwned` + `IAuditable`. **Index**: `ix_documents_user_session_id`.
- **Factory**: `Document.CreateForQuota(long sizeBytes, DocumentOrigin origin) : Result<Document>` —
  returns `quota.file_too_large`? No: per-file size is a config rule, enforced by `QuotaSnapshot`, not
  the domain. The factory validates `sizeBytes >= 0` and defaults `Status = Processing`. New documents
  are `Processing`; US-06 drives them to `Ready`/`Failed`.
- **Invariants** (domain-tested): `SizeBytes >= 0`; a freshly created document is `Processing` with the
  requested `Origin`; identity is a fresh GUID; `UserSessionId` is NOT set by the aggregate (stamped
  centrally). No quota arithmetic lives in the aggregate.
- **US-04/US-06/US-08 forward note**: the same row gains filename/storage pointer (US-04), richer
  processing transitions (US-06), and is removed by delete (US-08). US-05 touches only the columns above.

### Enums

```text
DocumentStatus { Processing = 0, Ready = 1, Failed = 2 }   # minimal; US-06 may extend
DocumentOrigin { User = 0, Demo = 1 }                       # Demo excluded from quota (US-03 seam)
```

## Value objects: `QuotaLimits` & `QuotaSnapshot` (Documents module, pure)

```text
QuotaLimits {
  int  MaxDocuments        # from QuotaOptions.MaxDocuments
  long MaxFileSizeBytes    # MaxFileSizeMb * 1_000_000
  long MaxTotalBytes       # MaxTotalMb   * 1_000_000
}

QuotaSnapshot {
  int  UsedDocuments
  long UsedBytes
  QuotaLimits Limits

  double        UsedMb        => round(UsedBytes / 1_000_000, 1)
  double        MaxTotalMb    => Limits.MaxTotalBytes / 1_000_000
  long          RemainingBytes=> max(0, MaxTotalBytes - UsedBytes)
  bool          IsFull        => UsedDocuments >= MaxDocuments || RemainingBytes == 0
  bool          CanUpload     => !IsFull

  Result CanAdmit(long fileSizeBytes):
    fileSizeBytes > MaxFileSizeBytes                 -> QuotaErrors.FileTooLarge
    UsedDocuments >= MaxDocuments                    -> QuotaErrors.QuotaExceeded
    UsedBytes + fileSizeBytes > MaxTotalBytes        -> QuotaErrors.TotalSizeQuotaExceeded
    else                                             -> Result.Success()
}
```

- `CanAdmit` is the single boundary rule, used by both the pre-check (`CheckCanUpload`) and the
  authoritative in-lock admit — they cannot drift. **Boundaries** (domain-tested): exactly at the count
  limit → exceeded; `UsedBytes + size == MaxTotalBytes` → admitted (only crossing is rejected).

## Configuration: `QuotaOptions` (bound from `Quota:*`)

| Key | Default | Meaning |
|---|---|---|
| `Quota:MaxDocuments` | `10` | max documents per session (`Failed` count, `Demo` excluded) |
| `Quota:MaxFileSizeMb` | `10` | max size of a single file (MB, decimal) |
| `Quota:MaxTotalMb` | `50` | max total storage per session (MB, decimal) |

- No magic numbers in code; MB→bytes uses decimal MB (`× 1_000_000`). "Quota-ready": a new tier is a
  config edit only.

## Application seams

```text
IQuotaService (Core, Documents module):
  Task<QuotaStateResponse> GetStateAsync(CancellationToken)                 # AC-1 read
  Task<Result>             CheckCanUpload(long sizeBytes, CancellationToken)# AC-2/AC-3 pre-check (non-atomic)
  Task<Result<Guid>>       TryAdmitAsync(long sizeBytes,                    # AC-5 atomic admit — US-04's entry point
                                          DocumentOrigin origin, CancellationToken)

IDocumentQuotaRepository (Core abstraction; EF impl in Infrastructure):
  Task<int>    CountAsync(CancellationToken)            # current session, excl. Demo
  Task<long>   SumSizeBytesAsync(CancellationToken)     # current session, excl. Demo
  Task<Result> TryAddWithinQuotaAsync(Document doc,     # advisory-lock tx: re-check under lock, insert or fail
                                       QuotaLimits limits, CancellationToken)
```

- Session scoping for every read is inherited from the global query filter — the seam adds no session
  predicate, only the `Origin != Demo` exclusion.
- `TryAdmitAsync` builds the `Document` via the factory and delegates to `TryAddWithinQuotaAsync`; the
  session id for the advisory-lock key comes from the injected `ISessionContext`.

## Read model

```text
QuotaStateResponse (GET /api/quota):
  int    usedDocuments
  int    maxDocuments
  double usedMb
  double maxTotalMb
  int    maxFileSizeMb
  bool   canUpload
```

## Persistence notes

- `RagBookDbContext` gains `DbSet<Document> Documents`; `Document` implements `ISessionOwned`, so the
  existing generic global query filter and central stamping apply automatically — no new wiring in the
  context beyond the DbSet and the `DocumentConfiguration` (table map + `user_session_id` index).
- Migration `AddDocuments` creates `documents`. Applied out-of-band (bundle/init/test fixture) — never
  at app startup (§VIII).
- The advisory lock uses `pg_advisory_xact_lock(<bigint key>)` executed on the same `DbContext`
  connection inside `BeginTransactionAsync`, so the lock and the insert share one transaction.

## State / lifecycle

- **Document**: `CreateForQuota` → `Processing` (stamped with current session) → visible only to that
  session and counted by the quota (unless `Demo`) → (US-06 → `Ready`/`Failed`; US-08 → deleted). A
  `Failed` document keeps counting until deleted.
- **Quota**: derived, not stored — recomputed per read/admit from the session's documents against
  `QuotaLimits`. A config limit change takes effect on the next evaluation; it never mutates documents.
