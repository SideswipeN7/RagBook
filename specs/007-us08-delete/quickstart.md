# Quickstart — Validate US-08

## Prerequisites

- .NET 10 SDK, Node.js (Angular 20), Docker running (Testcontainers pgvector).
- No migration (the cascade FK exists from US-06); `dotnet tool restore` only if you touch migrations.

## Run locally (Aspire)

```sh
cd src/Web && npm install && cd -
dotnet run --project src/RagBook.AppHost
# Upload a file, wait for it to index (US-06), then use the document row's "Usuń" action and confirm:
# the row disappears from the tree and the quota counter drops — no reload. Its chunks are gone.
```

## Automated validation (source of truth for DoD)

```sh
# Application tier (no Docker)
dotnet test tests/RagBook.Application.Tests   # DeleteDocumentCommandHandler — deleted vs not-found

# Integration tier — START DOCKER FIRST (Testcontainers pgvector)
dotnet test tests/RagBook.Api.IntegrationTests # cascade, 404 isolation, during-processing, storage-failure

# Frontend
cd src/Web && npm test                         # DocumentActionsStore: DELETE + refresh tree/quota; row confirm
```

Tests map to acceptance criteria:

| AC | Tier | Test | Proves |
|---|---|---|---|
| AC-1 | Application | `Should_ReturnNotFound_When_DocumentMissing` / `Should_Delete_When_Present` | 404 vs success |
| AC-1 | Web | `DocumentActionsStore deletes then refreshes tree + quota` | no-reload update (FR-005) |
| AC-2 | Integration | `Should_CascadeDeleteChunks_When_DocumentDeleted` | index gone (0 chunks with its id) |
| AC-3 | Integration | `Should_Delete_And_WorkerAbortsQuietly_When_ProcessingDocumentDeleted` | delete succeeds; later processing writes nothing, no error |
| AC-4 | Integration | `Should_Return404_When_DeletingAnotherSessionsDocument` | isolation; target untouched |
| AC-1 idempotent | Integration | `Should_Return404_When_DeletingTwice` | second delete → 404 |
| FR-004 | Integration | `Should_StillDelete_When_BlobRemovalFails` | storage failure tolerated; record + chunks gone |
| AC-1 | Web | `document leaf shows a Delete action and confirms before deleting` | confirm gate (FR-001) |

## Manual smoke (optional)

```sh
curl -i -c jar -b jar -X DELETE https://localhost:<api>/api/documents/<id>   # → 204
curl -i -c jar -b jar -X DELETE https://localhost:<api>/api/documents/<id>   # → 404 document.not_found (idempotent)
```

## Expected outcomes

- Deleting a document removes its record and **all** chunks (DB cascade); the tree drops the row and the
  quota drops without a reload.
- A cross-session or repeat delete returns 404, target untouched.
- Deleting a processing document succeeds; the worker aborts quietly (no chunks, no error).
- A blob-storage failure does not fail the delete (record + index gone; orphan logged).
