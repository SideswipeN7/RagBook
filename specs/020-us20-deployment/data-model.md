# Phase 1 Data Model — US-20

No entities, no schema, no migration authored. US-20 is packaging + docs + a small frontend addition. This lists the
artifacts and their config contract.

## Artifacts

| Artifact | Role |
|---|---|
| `src/RagBook.API/Dockerfile` | Multi-stage: SDK build → `api` target (aspnet runtime, runs `RagBook.API.dll` on :8080) + `migrate` target (runtime-deps, runs the EF bundle). |
| `src/Web/Dockerfile` | node:22 build (`npm ci && npm run build`) → nginx:alpine serving `dist/ragbook-web/browser` + `nginx.conf`. |
| `src/Web/nginx.conf` | SPA fallback (`try_files $uri /index.html`) + `location /api` → `proxy_pass http://api:8080` with `proxy_buffering off` (SSE). |
| `docker-compose.yml` | `postgres` (pgvector:pg17, healthcheck) · `migrate` (one-shot, EF bundle) · `api` · `web` (nginx, publishes :8080) · volumes `pgdata`, `blobs`. |
| `.env.example` | The required variables (below). |
| `.dockerignore` | Excludes `bin/`, `obj/`, `node_modules/`, `dist/`, `.git/`, `.blobstore/`. |
| `deploy/cloudbuild.yaml` + `deploy/README.md` | GCP build+push+deploy + documented steps. |
| `LICENSE` | MIT. |

## Config contract (`.env` / env vars, double-underscore form)

| Variable | Example / default | Secret? | Notes |
|---|---|---|---|
| `POSTGRES_USER` / `POSTGRES_PASSWORD` / `POSTGRES_DB` | `postgres` / `postgres` / `ragbookdb` | plain (local) | drives the postgres container + the connection string. |
| `ConnectionStrings__ragbookdb` | `Host=postgres;Port=5432;Database=ragbookdb;Username=postgres;Password=postgres` | plain (local) / secret (cloud) | consumed by `migrate` + `api`. |
| `Anthropic__ApplicationKey` | (empty) | **SECRET** | demo generation; empty ⇒ `chat.demo_unavailable`. |
| `Embedding__ApiKey` | (empty) | **SECRET** | empty ⇒ deterministic stand-in embedding; set ⇒ Voyage. |
| `Session__Secure` | `false` (local HTTP) / `true` (cloud HTTPS) | plain | over plain HTTP the secure cookie is dropped — false locally. |
| `Wolverine__DurabilityEnabled` | `true` | plain | enables outbox setup + the startup demo seed. |
| `FileStorage__RootPath` | `/data/blobs` | plain | mounted volume. |

## Compose service ordering

`postgres` (healthy) → `migrate` (runs EF bundle, exits 0) → `api` (waits `service_completed_successfully`; seeds
demo at startup) → `web` (nginx, the public origin on :8080).

## Frontend addition

- `chat` component: `suggestedQuestions: string[]` (2–3, matching demo docs), shown when `thread().length === 0`;
  `askSuggested(q)` sets the demo scope + calls `ask`. Karma-tested.
