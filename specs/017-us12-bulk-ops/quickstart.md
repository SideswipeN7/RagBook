# Quickstart — Validate US-12 (bulk operations)

Prerequisites: Docker (Testcontainers); .NET 10 SDK; Node + Edge/Chrome for Karma.

## 1. Application

```bash
dotnet test tests/RagBook.Application.Tests/RagBook.Application.Tests.csproj -c Debug
```
Proves: `BulkMove`/`BulkDelete` handlers — all-valid → apply; **one bad item rejects the whole set with no write**
(returns `failures[]`); de-dup; empty → `bulk_empty`; over-cap → `bulk_too_large`; move with a missing target folder
→ `folder.not_found` failure.

## 2. Integration (Testcontainers)

```bash
docker info >/dev/null 2>&1 || echo "start Docker first"
dotnet test tests/RagBook.Api.IntegrationTests/RagBook.Api.IntegrationTests.csproj -c Debug
```
Proves: `POST /api/documents/bulk-move` moves all selected docs into a folder; `POST /api/documents/bulk-delete`
removes records + chunks (cascade) and the **quota drops by N**; **all-or-nothing** — a selection with a foreign /
unknown id (or a read-only demo doc) → `422 document.bulk_validation_failed` + `failures[]` and **nothing changed**;
a foreign id is reported as `document.not_found`.

## 3. Frontend

```bash
cd src/Web && CHROME_BIN="/c/Program Files (x86)/Microsoft/Edge/Application/msedge.exe" \
  npm run test -- --watch=false --browsers=ChromeHeadless
```
Proves: the selection store toggles/clears/range-selects; the action bar shows the count + actions; "Usuń" opens a
design-system confirm (count + names) and calls `bulk-delete`; "Przenieś do…" calls `bulk-move`; a `422` marks the
offending items (`failedIds`); success clears the selection.

## 4. Manual (AppHost)

```bash
dotnet run --project src/RagBook.AppHost
```
- Tick 3 documents → the action bar shows "3 zaznaczonych". "Przenieś do…" → a folder → all three move.
- Tick 3 → "Usuń" → confirm → they're gone and the quota drops by 3.
- Include a document then delete it in another tab, then bulk-delete → the operation is refused, that item is
  flagged, nothing else is deleted.

## Acceptance mapping

| AC | Validated by |
|---|---|
| AC-1 select + action bar | §3 selection/action bar · §4 |
| AC-2 bulk move | §2 bulk-move · §3 picker |
| AC-3 bulk delete (+ quota) | §2 delete cascade + quota · §3 confirm |
| AC-4 all-or-nothing | §1/§2 nothing-changed + failures · §3 marks |
| AC-5 per-id ownership | §2 foreign id → not-found, refused |
