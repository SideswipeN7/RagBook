# Quickstart — Validate US-10 (drag & drop move)

Prerequisites: Docker (Testcontainers); .NET 10 SDK; Node + Edge/Chrome for Karma.

## 1. Domain + Application

```bash
dotnet test tests/RagBook.Domain.Tests/RagBook.Domain.Tests.csproj -c Debug
dotnet test tests/RagBook.Application.Tests/RagBook.Application.Tests.csproj -c Debug
```
Proves: `Document.MoveToFolder` sets the folder (incl. root/null); `MoveDocumentCommandHandler` branches —
not-found, read-only (demo), folder-not-found, no-op (same folder → no save), and a successful move.

## 2. Integration (Testcontainers)

```bash
docker info >/dev/null 2>&1 || echo "start Docker first"
dotnet test tests/RagBook.Api.IntegrationTests/RagBook.Api.IntegrationTests.csproj -c Debug
```
Proves: `PATCH /api/documents/{id}/folder` moves to a folder and to the root (`folderId:null`); `document.not_found`
/ `folder.not_found` / `document.read_only`; **the document's chunks are unchanged** after a move (SC-003);
cross-session move → 404.

## 3. Frontend

```bash
cd src/Web && CHROME_BIN="/c/Program Files (x86)/Microsoft/Edge/Application/msedge.exe" \
  npm run test -- --watch=false --browsers=ChromeHeadless
```
Proves: `TreeStore.moveDocument` optimistically moves a document and **rolls back** on a failed `PATCH` (with a
notice); a drop onto the current folder issues no request; the "Przenieś do…" menu calls the same `moveDocument`.

## 4. Manual (AppHost)

```bash
dotnet run --project src/RagBook.AppHost
```
- Drag a root document onto a folder → it appears there instantly; reload → it stays.
- Drag it back onto the root zone → it leaves the folder.
- Force a failure (e.g. delete the folder in another tab first) → the document snaps back + a notice.
- Use a document's "Przenieś do…" menu → identical move without the mouse-drag.

## Acceptance mapping

| AC | Validated by |
|---|---|
| AC-1 drag onto folder (optimistic + persisted) | §2 move + §3 optimistic · §4 |
| AC-2 rollback on failure | §3 rollback + notice |
| AC-3 drop-target feedback | §3 component highlight · §4 |
| AC-4 move to root | §2 `folderId:null` · §3 |
| AC-5 menu fallback (same action) | §3 menu → `moveDocument` |
