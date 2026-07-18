# Phase 0 Research — US-20 Deployment & case study

## D1 — Container topology: nginx front + API + Postgres (clarify Q1)

**Decision**: An **nginx** image serves the Angular static build (`dist/ragbook-web/browser`) and reverse-proxies
`/api` → the API container (`api:8080`); the browser talks to nginx only (single origin). The .NET API is **not**
modified to serve static files. `nginx.conf`: `location /api { proxy_pass http://api:8080; }` with
`proxy_buffering off;`, `proxy_read_timeout` high, and `proxy_set_header` for the SSE route so tokens stream; a SPA
fallback `try_files $uri /index.html;` for everything else.

**Rationale**: Preserves the app's same-origin assumption (all calls are relative `/api`, SSE via `fetch`), needs no
CORS, and keeps the API image lean. Chosen by the user over the API-serves-`dist/` single-image option.

**Alternatives rejected**: API serving `dist/` (one image, but the user prefers separated concerns); a CORS setup
(cross-origin cookies + preflight complexity for a same-origin SPA).

## D2 — File storage: LocalFileStorage + volume; GCS as future work (clarify Q2)

**Decision**: Keep `LocalFileStorage`; mount a Docker **volume** at `FileStorage:RootPath` (e.g. `/data/blobs`) so
uploads persist across restarts locally. The cloud deploy doc mounts a disk/volume and lists a **GCS `IFileStorage`
adapter** as the productionization step (future work).

**Rationale**: Fully verifiable locally, keeps the story to packaging; a GCS adapter needs the SDK + credentials +
its own tests and can't be exercised here.

## D3 — Migrations: a separate EF migration-bundle compose step (§VIII)

**Decision**: The API `Dockerfile` build stage produces a self-contained **EF migration bundle**
(`dotnet ef migrations bundle --project src/RagBook.Infrastructure.Migrations --startup-project src/RagBook.API -o
/app/efbundle -r linux-x64 --self-contained`); a `migrate` Dockerfile target ships just the bundle. A one-shot
compose `migrate` service runs it against the DB (`./efbundle --connection "$CONN"`) with `depends_on: postgres
(healthy)`; `api` waits for `migrate` to complete (`service_completed_successfully`). Migrations therefore run as a
deploy step, never at app startup (constitution §VIII). The `RagBookDbContextFactory` design-time factory makes the
bundle buildable without a live DB.

**Rationale**: The MS-recommended production migration path; the bundle is a single native executable, no SDK in the
runtime image. `CREATE EXTENSION vector` lives in a migration, so the pgvector image is required (it provides the
extension binary; the migration only enables it).

**Alternatives rejected**: migrating at app startup (violates §VIII, unsafe multi-instance); running `dotnet ef` from
an SDK image against mounted source (slow, needs restore in the container); a hand-written SQL init script (drifts
from the EF model).

## D4 — Demo seed on both environments

**Decision**: Run the compose `api` service with `Wolverine__DurabilityEnabled=true` so the existing startup demo
seed (US-03, idempotent by fixed id) runs after `migrate` completes — demo works immediately on a fresh DB and is a
no-op on restart. The demo application key + embedding key come from `.env` (env vars, double-underscore form).

**Rationale**: Reuses the shipped idempotent seeder; `migrate` → `api` ordering guarantees the schema exists first.

## D5 — Suggested demo questions (AC-3)

**Decision**: The chat empty-state (when `thread().length === 0`) renders 2–3 **suggested-question chips** whose text
matches the seeded demo documents (the demo lease + technical doc); clicking a chip calls a new `askSuggested(q)` on
the chat component that sets the demo scope and calls `store.ask(q, demoScope)`. A Karma test asserts the chips
render on an empty thread and that a click asks in the demo scope.

**Rationale**: The minimal, testable frontend piece that delivers the one-minute evaluator path; reuses the existing
`ask` + demo scope + `DemoStore`.

## D6 — Case-study README + repo hygiene

**Decision**: Rewrite the top of `README.md` into the AC-4 case-study structure (problem + a demo visual placeholder,
a **mermaid** architecture diagram, "Design decisions" each with a rejected alternative, the RAG pipeline, the
error-code catalog lifted from `docs/features/README.md` (US-19), known limitations / future work, run instructions
for local compose + GCP), and correct the stale "US-01 + US-05" framing. Add an **MIT `LICENSE`**, add `.blobstore/`
+ the compose bind path to `.gitignore`, keep secrets out (a history scan in the review), and add a **docker-build
CI job** (build the API + web images) so the Dockerfiles are verified on every PR. `.dockerignore` keeps build
contexts small.

**Rationale**: The README is the primary case-study artifact; hygiene + a green image-build make the public repo
credible. Much raw material already exists in the current README/feature docs to consolidate.
