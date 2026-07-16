# Quickstart — Validate US-11 (move folders with subtree)

Prerequisites: Docker (Testcontainers); .NET 10 SDK; Node + Edge/Chrome for Karma.

## 1. Domain + Application

```bash
dotnet test tests/RagBook.Domain.Tests/RagBook.Domain.Tests.csproj -c Debug
dotnet test tests/RagBook.Application.Tests/RagBook.Application.Tests.csproj -c Debug
```
Proves: cycle via `FolderPath.IsPrefixOf`; subtree-depth math; `MoveFolderCommandHandler` branches — not-found,
circular, max-depth, duplicate-name, no-op (same parent → no write), and a valid move.

## 2. Integration (Testcontainers)

```bash
docker info >/dev/null 2>&1 || echo "start Docker first"
dotnet test tests/RagBook.Api.IntegrationTests/RagBook.Api.IntegrationTests.csproj -c Debug
```
Proves: `PATCH /api/folders/{id}/parent` moves `Umowy/2026` into `Archiwum` and **every descendant's `path` is
rewritten** (Archiwum/Umowy/2026); move to root (`parentId:null`); `folder.circular_move` / `max_depth_exceeded` /
`duplicate_name`; **documents untouched** (their `folder_id` unchanged before/after); a chat scope over the new
ancestor **includes the moved documents**; cross-session move → 404 (and the bulk update never crosses sessions).

## 3. Frontend

```bash
cd src/Web && CHROME_BIN="/c/Program Files (x86)/Microsoft/Edge/Application/msedge.exe" \
  npm run test -- --watch=false --browsers=ChromeHeadless
```
Proves: `TreeStore.moveFolder` optimistically re-parents (subtree re-nests) and **rolls back** on a failed
`PATCH`; `isDescendant` excludes the moved folder + its subtree; `onDrop` routes folder-vs-document by `kind`; the
"Przenieś do…" folder menu calls the same `moveFolder`.

## 4. Manual (AppHost)

```bash
dotnet run --project src/RagBook.AppHost
```
- Drag `Umowy` (with `2026` inside) onto `Archiwum` → the whole branch appears under `Archiwum`; reload → it stays.
- Try to drag `Umowy` onto its own `2026` → `2026` never highlights.
- Drag `Umowy` onto the root zone → it becomes top-level.
- Use a folder's "Przenieś do…" menu → identical move without the mouse-drag.

## Acceptance mapping

| AC | Validated by |
|---|---|
| AC-1 move with subtree (+ chat scope) | §2 subtree-path + chat-scope · §4 |
| AC-2 no cycles | §1 IsPrefixOf · §2 circular · §3 isDescendant predicate |
| AC-3 depth / AC-4 duplicate name | §1/§2 guards |
| AC-5 optimistic + rollback | §3 moveFolder rollback |
| (move to root) | §2 `parentId:null` · §3 |
