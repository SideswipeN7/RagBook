# Quickstart — Validate US-05

## Prerequisites

- .NET 10 SDK, Node.js (Angular), Docker running (for integration tests / Aspire PostgreSQL).

## Run locally (Aspire)

```sh
cd src/Web && npm install && cd -        # install SPA deps once
dotnet run --project src/RagBook.AppHost
# Aspire dashboard prints its URL; it starts PostgreSQL, the API, and the Angular dev server.
# The quota-bar renders "X / 10 plików" and "X / 50 MB" for the current session.
```

## Automated validation (the source of truth for DoD)

```sh
# Cheapest tiers first (no Docker)
dotnet test tests/RagBook.Domain.Tests            # QuotaSnapshot boundaries (AC-2/AC-3), Document invariants
dotnet test tests/RagBook.Application.Tests        # GetQuotaQueryHandler, QuotaService (mocked repo)

# Integration tier — START DOCKER FIRST (Testcontainers PostgreSQL)
dotnet test tests/RagBook.Api.IntegrationTests     # AC-1 read, AC-5 concurrency, demo exclusion

# Frontend
cd src/Web && npm test                             # quota-bar renders counts + full-state hint
```

Tests map to acceptance criteria:

| AC | Tier | Test (`Should_..._When_...`) | Proves |
|---|---|---|---|
| AC-2 | Domain | `Should_ReturnQuotaExceeded_When_DocumentCountAtLimit` | at 10 docs → `quota.exceeded`, nothing admitted |
| AC-3 | Domain | `Should_ReturnTotalSizeExceeded_When_AddingFileWouldCrossTotal` | 45 MB + 8 MB > 50 MB → `quota.total_size_exceeded` |
| boundary | Domain | `Should_Admit_When_UsageExactlyAtLimitBoundary` | exactly-at-limit admitted; only crossing rejected |
| guard | Domain | `Should_ReturnFileTooLarge_When_FileExceedsPerFileMax` | file > `MaxFileSizeMb` → `quota.file_too_large` |
| invariant | Domain | `Should_CreateProcessingUserDocument_When_CreatedForQuota` | factory: `Processing`, requested origin, size ≥ 0, session unset |
| AC-1 | Application | `Should_ReturnUsedCountAndMb_When_GettingQuotaState` | state maps count/usedMb/limits/canUpload |
| AC-2/3 | Application | `Should_FailFast_When_CheckCanUploadExceedsQuota` | `CheckCanUpload` returns the right code without a write |
| AC-1 | Integration | `Should_ReportCountAndUsage_When_SessionHasDocuments` | real COUNT/SUM over seeded rows via `GET /api/quota` |
| FR-007 | Integration | `Should_ExcludeDemoDocuments_When_CountingQuota` | `Demo` origin not counted; `Failed` counted |
| **AC-5** | Integration | `Should_AdmitAtMostOneDocument_When_TwoUploadsRaceAtLimit` | two concurrent admits at 9/10 → count == 10, exactly one success |

## AC-4 (frees up after delete) & AC-2 UI note

- Delete (US-08) and upload (US-04) are not built here. AC-4's "counter drops, upload re-enabled" is
  validated in US-05 by driving the count/size seam directly (remove a row → `GET /api/quota` reflects
  the freed slot and `canUpload` returns true) and end-to-end once US-04/US-08 land.
- The `QuotaStore.refresh()` signal is the shared hook US-04/US-08 call so the quota-bar updates
  without a page reload; the quota-bar shows the "Limit osiągnięty — usuń pliki" hint when `isFull`.

## Manual smoke (optional)

```sh
curl -i -c jar -b jar http://localhost:<api>/api/quota   # → 200 {"usedDocuments":0,"maxDocuments":10,...}
# Lower Quota:MaxDocuments below current usage in config → existing docs stay, GET /api/quota shows canUpload:false
```

## Expected outcomes

- `GET /api/quota` reports the session's own usage against the configured limits; other sessions never
  affect it.
- Admitting a file that would cross either limit fails with the correct code before anything is written.
- Under two concurrent admits at 9/10, the document count never exceeds 10 — at most one wins.
- Lowering a config limit below current usage deletes nothing; it only blocks new admits.
