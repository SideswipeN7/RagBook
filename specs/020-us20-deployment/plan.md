# Implementation Plan: Deployment i pakiet case study (US-20)

**Branch**: `020-us20-deployment` | **Date**: 2026-07-18 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/020-us20-deployment/spec.md`

## Summary

Package the finished product for a one-command local run and a documented cloud deploy, and turn the README into a
case study. The container topology (clarify Q1) is **nginx front + API + Postgres**: an nginx image serves the
Angular static build and reverse-proxies `/api` to the API (single origin, no CORS, SSE with buffering off) — the
.NET API is unchanged (no static-serving code). Migrations run as a **separate compose step** (an EF migration
bundle, §VIII), then the API starts and (with durability on) seeds the demo docs idempotently. File storage stays
`LocalFileStorage` on a mounted volume; a GCS adapter is documented future work (clarify Q2). Adds a multi-stage
API Dockerfile (+ a `migrate` target producing the bundle), a Web Dockerfile (Angular build → nginx + `nginx.conf`),
`docker-compose.yml`, `.env.example`, a GCP deploy doc + `cloudbuild`/script, a chat **empty-state with suggested
demo questions** (AC-3, the only new tested code), the **case-study README**, a `LICENSE`, `.gitignore` hardening,
and a CI docker-build job.

## Technical Context

**Language/Version**: C# (.NET 10) API; Angular 20 SPA; nginx; PostgreSQL (pgvector pg17); Docker / docker-compose.

**Primary Dependencies**: the existing app (Aspire dev unchanged); EF migrations project (`RagBook.Infrastructure
.Migrations`, `CREATE EXTENSION vector` in a migration); the demo seed (US-03, startup-gated on durability); the
chat component + `DemoStore` (US-03) for the suggested-questions empty-state.

**Storage**: PostgreSQL (pgvector image required for `CREATE EXTENSION`); uploaded files on a Docker **volume**
(`LocalFileStorage`). No schema change / no migration authored here (existing migrations are applied by the step).

**Testing**: Angular Karma (the suggested-question chips: empty-state renders them, click → `ask` in demo scope);
the four existing tiers stay green; `docker compose config` validates the stack; the Docker image builds (multi-stage)
verify the Dockerfiles — added as a CI job.

**Target Platform**: Docker locally; GCP Cloud Run + Cloud SQL (pgvector) + Secret Manager in the cloud (documented).

**Project Type**: Web (containerised modular-monolith API + nginx-served SPA).

**Performance Goals**: `docker compose up` → app on localhost in one command; evaluator reaches a cited demo answer
in under a minute.

**Constraints**: migrations off the app-start path (§VIII); secrets only via env / Secret Manager (§VII); same-origin
via nginx (relative `/api`, SSE buffering off); Aspire dev workflow untouched; demo seed idempotent on both
environments; `Session:Secure=false` for local plain-HTTP (documented in `.env.example`).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **I. Vertical-Slice Modular Monolith** ✅ — no module changes; packaging + a small frontend empty-state addition.
- **II. CQRS + Result Contract** ✅ — untouched.
- **III. Data Isolation** ✅ — untouched; demo remains global-read-only, user data session-scoped.
- **IV. Test-First** ✅ — the only new logic is the suggested-question chips → a Karma test (empty-state renders,
  click asks in demo scope). Infra is validated by `docker compose config` + image builds (CI job); docs by review.
- **V. Providers** ✅ — embedding/generation config-driven; missing secrets degrade gracefully (fake embedding /
  demo-unavailable), documented in `.env.example`.
- **VI. Auditing & Time** ✅ — n/a. **VII. Secrets** ✅ — `.env.example` + Secret Manager only; no secret committed;
  `.gitignore` hardened; a secret-scan of history in the review.
- **VIII. Ops & Delivery** ✅ — migrations run as a **separate compose step** (EF bundle), never at app startup; the
  README documents the choice; CI builds + tests every PR (+ a new image-build job).
- **IX. Frontend & Design System** ✅ — the suggested-question chips use design tokens, ≥360px, are keyboard-usable;
  no native dialogs.

**Result: PASS** — no violations; Complexity Tracking empty. Both clarified decisions (nginx front; LocalFileStorage
+ volume with GCS as future work) keep the story focused and locally verifiable.

## Project Structure

### Documentation (this feature)

```text
specs/020-us20-deployment/
├── plan.md, research.md, data-model.md, quickstart.md
├── contracts/deployment.md
├── checklists/requirements.md
└── tasks.md   (/speckit-tasks)
```

### Source Code (repository root)

```text
src/RagBook.API/Dockerfile          # multi-stage: SDK build → publish (api target) + EF migration bundle (migrate target)
src/Web/Dockerfile                  # node build Angular → nginx serving dist + reverse-proxy
src/Web/nginx.conf                  # SPA fallback + proxy /api → api:8080 (proxy_buffering off for SSE)
docker-compose.yml                  # postgres(pgvector) + migrate(one-shot) + api + web(nginx) + files volume + .env
.env.example                        # ConnectionStrings__ragbookdb, Anthropic__ApplicationKey, Embedding__ApiKey, Session__Secure=false …
.dockerignore                       # keep bin/obj/node_modules/dist out of build contexts
deploy/
├── cloudbuild.yaml                 # GCP build+push+deploy (documented, not executed here)
└── README.md                       # GCP steps: Cloud Run + Cloud SQL pgvector + Secret Manager + SSE/timeout note
.github/workflows/ci.yml            # + a docker-build job (builds the API + web images to verify the Dockerfiles)
.gitignore                          # + .blobstore/ (and any bind-mount path)
LICENSE                             # MIT

src/Web/src/app/chat/
├── chat.ts / chat.html / chat.scss # empty-state: 2–3 suggested demo questions → askSuggested() → ask in demo scope
└── chat.spec.ts                    # chips render when thread empty; click → ask

README.md                           # rewritten as a case study (problem+visual, mermaid arch, decisions+rejected alt,
                                    #   RAG pipeline, error-code catalog, limitations/future work, run instructions)
```

**Structure Decision**: An nginx front (static SPA + `/api` reverse proxy) fronts the API; the API image also builds
an EF **migration bundle** run by a one-shot compose `migrate` service before the API starts. Files persist on a
volume (`LocalFileStorage`). The README becomes the case study. The only new app code is the chat empty-state chips
(Karma-tested); everything else is config/docs validated by `compose config`, image builds, and review. No migration
authored.

## Complexity Tracking

*No constitution violations — no entries.*
