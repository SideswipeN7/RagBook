# Quickstart — Validate US-07

## Prerequisites

- .NET 10 SDK, Node.js (Angular 20), Docker running (integration tests / Aspire PostgreSQL).
- `dotnet tool restore` before creating/applying the migration (see AGENTS.md).
- `npm install` in `src/Web` (adds `@angular/cdk`).

## Run locally (Aspire)

```sh
cd src/Web && npm install && cd -
dotnet run --project src/RagBook.AppHost
# The main view shows one tree: folders (expand/collapse) with their documents inside and root
# documents at the top. Upload a file (US-04) and it appears without a reload; a fresh session shows
# the empty state with the "upload your first document" CTA + demo pointer.
```

## Automated validation (source of truth for DoD)

```sh
# Application tier (no Docker)
dotnet test tests/RagBook.Application.Tests         # GetTreeQueryHandler — composes + passes through ordered lists

# Integration tier — START DOCKER FIRST (Testcontainers PostgreSQL)
dotnet test tests/RagBook.Api.IntegrationTests      # GET /api/tree: compose, ordering, isolation, single-request

# Frontend
cd src/Web && npm test                              # TreeStore compose + expansion persistence; tree render + statuses + empty state; size formatter
```

Tests map to acceptance criteria:

| AC | Tier | Test | Proves |
|---|---|---|---|
| AC-5 | Integration | `Should_ReturnFoldersAndDocuments_InOneResponse` | one `GET /api/tree` returns both lists (no N+1) |
| AC-1 | Integration | `Should_OrderFoldersAlphabeticallyAndDocumentsByDateDesc` | FR-008 ordering |
| FR-012 | Integration | `Should_ExcludeOtherSessionsData` | cross-session folders/documents absent |
| FR-013 | Integration | `Should_ExcludeDemoDocuments` | demo-origin docs not in the session tree |
| AC-1 | Web | `TreeStore composes nested tree with root documents at top` | hierarchy + root placement |
| AC-1 | Web | `expansion state persists to sessionStorage` | collapse survives navigation (SC-005) |
| AC-2 | Web | `document row shows name, size, status, chunk count, date` | metadata (FR-005) |
| AC-2 | Web | `processing shows spinner; failed shows reason; ready shows chunks` | status rendering (FR-006) |
| AC-2 | Web | `failed without reason shows generic message` | edge case |
| AC-2 | Web | `long name truncates with title tooltip` | FR-010 |
| AC-3 | Web | `empty session renders empty state with CTA + demo pointer` | FR-007 |
| AC-4 | Web | `refresh() re-reads /api/tree` | no-reload update (FR-009/SC-006) |
| FR-005 | Web | `formatFileSize: B/KB/MB decimal 1dp` | size util |
| FR-011 | Web | `empty folder is expandable and shows empty note` | edge case |

## Manual smoke (optional)

```sh
curl -s -c jar -b jar https://localhost:<api>/api/tree | jq
# → { "folders": [...A→Z...], "documents": [...newest first...] }
# upload two files + create nested folders → both appear in the tree; collapse a folder, reload the SPA
# route → still collapsed (sessionStorage).
```

## Expected outcomes

- The whole view loads from **one** request; folders A→Z, documents newest-first; root documents at top.
- Each document row shows name, decimal size, status (spinner/error+reason/chunk count), chunk count, date.
- A fresh session shows the empty state; uploads/deletes refresh the tree without a reload.
- Only the current session's data appears; demo documents are not mixed in.
