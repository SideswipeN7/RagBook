# Tasks: Deployment i pakiet case study (US-20)

**Input**: Design documents from `specs/020-us20-deployment/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/deployment.md, quickstart.md

**Tests**: Frontend chips get a Karma test (the only new logic); infra validated by `docker compose config` + image
builds; the four existing tiers stay green.

**Organization**: Grouped by user story. Foundational = the container images + compose.

## Format: `[ID] [P?] [Story] Description`

- **[Story]**: US1 (one-command run), US2 (evaluator path), US3 (README case study), US4 (cloud deploy), US5 (hygiene).

---

## Phase 1: Setup

- [x] T001 Confirm branch `fm/us20-deployment` off master (US-19 merged `454e705`); Docker available for `compose config`/build validation.
- [x] T002 [P] `.dockerignore` (repo root) excluding `bin/`, `obj/`, `node_modules/`, `dist/`, `.git/`, `.blobstore/`, `.angular/`.

---

## Phase 2: Foundational (container images + compose)

- [x] T003 `src/RagBook.API/Dockerfile` — multi-stage: `sdk:10.0` build (restore `RagBook.slnx`, `dotnet publish src/RagBook.API`), `dotnet tool restore` + `dotnet ef migrations bundle --project src/RagBook.Infrastructure.Migrations --startup-project src/RagBook.API -o /app/efbundle -r linux-x64 --self-contained`; `api` target (`aspnet:10.0`, `ENTRYPOINT dotnet RagBook.API.dll`, :8080); `migrate` target (`runtime-deps:10.0`, `ENTRYPOINT ./efbundle`).
- [x] T004 [P] `src/Web/Dockerfile` — `node:22` build (`npm ci && npm run build`), then `nginx:alpine` COPY `dist/ragbook-web/browser` → `/usr/share/nginx/html` + `nginx.conf`.
- [x] T005 [P] `src/Web/nginx.conf` — SPA fallback (`try_files $uri /index.html`); `location /api { proxy_pass http://api:8080; proxy_buffering off; proxy_read_timeout 3600s; proxy_set_header Host $host; }` (SSE-safe).
- [x] T006 `docker-compose.yml` — `postgres` (pgvector/pgvector:pg17, healthcheck `pg_isready`, `pgdata` volume), `migrate` (build `src/RagBook.API` target `migrate`, `command: ["--connection","${ConnectionStrings__ragbookdb}"]`, `depends_on: postgres: healthy`), `api` (build target `api`, env from `.env`, `FileStorage__RootPath=/data/blobs`, `blobs` volume, `depends_on: migrate: service_completed_successfully`), `web` (build `src/Web`, `ports 8080:80`, `depends_on: api`).
- [x] T007 [P] `.env.example` — `POSTGRES_*`, `ConnectionStrings__ragbookdb`, `Anthropic__ApplicationKey=`, `Embedding__ApiKey=`, `Session__Secure=false`, `Wolverine__DurabilityEnabled=true`, `FileStorage__RootPath=/data/blobs`, with comments.

**Checkpoint**: `docker compose config` valid; `docker compose build` builds both images.

---

## Phase 3: User Story 1 — One-command local run (P1) 🎯 MVP

- [x] T008 [US1] Validate `docker compose config` resolves; `docker compose build` succeeds (both images). Fix any Dockerfile/compose issue (paths, targets, bundle build).
- [x] T009 [US1] (If Docker time allows) `docker compose up` and confirm: migrate exits 0, api starts + seeds demo, `curl http://localhost:8080/` serves the SPA and `http://localhost:8080/api/demo/status` responds. Document the observed run in the PR notes.

**Checkpoint**: one-command local run works.

---

## Phase 4: User Story 2 — Evaluator path: suggested demo questions (P1)

- [x] T010 [P] [US2] Karma: chat empty-state renders 2–3 suggested-question chips when `thread().length === 0`; clicking one calls `ask` in the demo scope. (FAIL first.) `src/Web/src/app/chat/chat.spec.ts`.
- [x] T011 [US2] `chat.ts`: `suggestedQuestions` (2–3, matching the demo docs) + `askSuggested(q)` (set demo scope + `store.ask`); `chat.html`: an empty-state block (`@if (thread().length === 0)`) with the chips; `chat.scss`: chip styles (tokens, ≥360px). Wire the demo scope so the suggested question runs keyless.

**Checkpoint**: empty-state chips deliver the one-minute demo path.

---

## Phase 5: User Story 3 — README case study (P1)

- [x] T012 [US3] Rewrite the top of `README.md` into the AC-4 structure: (1) problem + demo visual (placeholder/note), (2) **mermaid** architecture diagram, (3) design decisions each with a rejected alternative (materialized path vs CTE; pgvector pre-filtering; BYOK no-persistence; grounding threshold + sentinel; single embedding model; all-or-nothing bulk), (4) RAG pipeline step-by-step, (5) error-code catalog (reference/lift from `docs/features/README.md`), (6) known limitations / future work, (7) run instructions (local compose + GCP). Correct the stale "US-01 + US-05" framing.

**Checkpoint**: README reads as a case study.

---

## Phase 6: User Story 4 — Documented cloud deploy (P2)

- [x] T013 [P] [US4] `deploy/README.md` — GCP steps: build/push images (Artifact Registry), Cloud Run (api + nginx front), Cloud SQL PostgreSQL + `CREATE EXTENSION vector`, Secret Manager for the keys + connection, run the `migrate` bundle as a Cloud Run Job before deploy, SSE within the request timeout (`Rag:StreamHeartbeatSeconds`), `Session__Secure=true`, cold-start note.
- [x] T014 [P] [US4] `deploy/cloudbuild.yaml` — a documented (not executed) build+push+deploy pipeline for the two images + the migrate job.

**Checkpoint**: the cloud path is documented and correct.

---

## Phase 7: User Story 5 — Repo hygiene (P1)

- [x] T015 [P] [US5] `LICENSE` (MIT) at the repo root.
- [x] T016 [P] [US5] `.gitignore` — add `.blobstore/` (and the compose bind path if any).
- [x] T017 [P] [US5] Add a **docker-build** job to `.github/workflows/ci.yml` (build the API `api` target + the web image; no push) so both Dockerfiles are verified on every PR.
- [x] T018 [US5] Secret scan: `git log -p -- '*.json' '*.cs' '*.yml' | grep -iE "sk-ant|voyage|AIza|BEGIN .*PRIVATE"` → confirm none; confirm `Anthropic:ApplicationKey`/`Embedding:ApiKey` absent from committed appsettings.

---

## Phase 8: Polish & Verification

- [x] T019 [P] Update `docs/features/README.md` milestone/status to 20/20 (roadmap complete) if it tracks it.
- [x] T020 Run all 4 tiers green (Domain/Application/Integration-Testcontainers/Angular-Karma) + `docker compose config`/`build`; then critical diff review before the PR.

---

## Dependencies & Execution Order

- **Setup (T001–T002)** → **Foundational (T003–T007)** blocks the run. T003 (API+bundle) and T004/T005 (web+nginx) parallel; T006 (compose) after; T007 (.env) parallel.
- **US1 (T008–T009)** after Foundational. **US2 (T010–T011)** frontend, independent. **US3 (T012)** docs, independent. **US4 (T013–T014)** docs, parallel. **US5 (T015–T018)** parallel.
- **Polish (T019–T020)** last.

## Implementation Strategy

**MVP** = US1 (one-command run) + US2 (suggested questions) + US3 (case-study README) — the evaluator-facing core.
Build the images + compose (Foundational), validate `compose config`/`build` (and `up` if time allows), add the
empty-state chips + Karma, rewrite the README, add the cloud-deploy docs + hygiene (LICENSE, .gitignore, CI
image-build, secret scan), then the full green run + critical review before the final PR that completes the roadmap.
