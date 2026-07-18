# Feature Specification: Deployment i pakiet case study (US-20)

**Feature Branch**: `020-us20-deployment`

**Created**: 2026-07-18

**Status**: Draft

**Input**: User description: US-20 — an evaluator (recruiter / client) opens a public link and within a minute sees
a working demo chat with citations, and finds the architecture and design decisions in the README — without cloning
the repo. Package the project for one-command local run and documented cloud deployment.

## Context

RagBook is feature-complete (19/20 stories merged). US-20 is the **product wrapper**: make it trivially runnable and
present it as a case study. Today the app runs only via .NET Aspire in dev (Postgres/pgvector + API + `ng serve`
proxy); there is **no** Dockerfile, docker-compose, `.env.example`, or `LICENSE`, the API does not serve the Angular
build (dev relies on a same-origin proxy), migrations run only out-of-band (constitution §VIII), and the README is a
rich but stale ("US-01 + US-05") per-feature document rather than a case study. This story adds a **single-command
local run** (`docker compose up`), a **documented cloud deployment** (GCP Cloud Run + Cloud SQL pgvector + Secret
Manager), a **case-study README**, an evaluator-friendly **chat empty-state with suggested demo questions**, and
**repo hygiene** (LICENSE, secret-free, CI on PRs). Secrets come only from env / Secret Manager; the demo seed
(US-03) is idempotent on both environments; migrations remain a separate step.

## Clarifications

### Session 2026-07-18

- Q: How should the frontend be served in the container / production? → A: **Separate nginx + reverse proxy** — an
  `nginx` container serves the Angular static build and reverse-proxies `/api` to the API container, so the browser
  hits a single origin (nginx) and the app's relative `/api` calls + SSE work unchanged. The .NET API is **not**
  changed to serve static files; nginx owns the static + proxy concern. SSE requires `proxy_buffering off` on the
  chat route so tokens stream. (Rejected: the API serving `dist/` — one image, but the user prefers separated
  concerns.)
- Q: What's the scope for cloud file storage (uploads)? → A: **Keep `LocalFileStorage` + a mounted volume**; a real
  object-storage (GCS) `IFileStorage` adapter is documented as **future work**, not built now. Local run mounts a
  volume; the cloud deploy doc uses a mounted disk/volume and notes the GCS adapter as the productionization step.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - One-command local run (Priority: P1) 🎯 MVP

On a clean machine with Docker, a developer copies `.env.example` to `.env` (filling a couple of keys) and runs one
command; the whole app comes up on localhost with the database migrated, demo documents seeded, and demo mode
working immediately.

**Why this priority**: A zero-friction local run is the baseline for anyone evaluating or contributing; without it
the project is not "runnable".

**Independent Test**: On a clean Docker host, `cp .env.example .env` then `docker compose up` → the app is reachable
on localhost, migrations + demo seed have run, and a demo question streams an answer.

**Acceptance Scenarios**:

1. **Given** a clean machine with Docker and a filled `.env`, **When** the developer runs the single compose
   command, **Then** the app serves on localhost with the database migrated and demo documents seeded, and demo
   mode works without any further configuration.
2. **Given** the compose file, **When** its configuration is validated, **Then** it references the pgvector image,
   a migration step separate from app startup, a persistent volume for uploaded files, and env from `.env`.

---

### User Story 2 - Evaluator path in under a minute (Priority: P1)

A visitor opens the public URL and, with no configuration, sees the demo documents, is offered two or three ready
questions, and on clicking one gets a streaming answer with clickable citations.

**Why this priority**: This is the story's headline — the recruiter's one-minute "it works" experience.

**Independent Test**: Open the app with no key set; the chat empty-state shows suggested demo questions; clicking one
sends it in the demo scope and a streamed answer with clickable citations appears.

**Acceptance Scenarios**:

1. **Given** the app open with no API key, **When** the chat has no messages yet, **Then** two or three suggested
   demo questions (matching the demo documents) are shown, and clicking one asks it in the demo scope and streams an
   answer with clickable citations.

---

### User Story 3 - README as a case study (Priority: P1)

A reader opens the README and finds the problem statement + a demo visual up top, an architecture diagram, the key
design decisions (each with its rejected alternative), the RAG pipeline, the error-code catalog, known limitations,
and run instructions — enough to understand the project without reading the code.

**Why this priority**: The README is the primary case-study artifact; it's what an evaluator reads instead of the
code.

**Independent Test**: The README contains, in order: problem + demo visual, an architecture diagram, a "Design
decisions" section (materialized path vs CTE; pgvector pre-filtering; BYOK without secret persistence; grounding
threshold + refusal sentinel; single embedding model; all-or-nothing bulk — each with a rejected alternative), the
RAG pipeline step by step, the error-code catalog, known limitations / future work, and run instructions (local +
cloud).

**Acceptance Scenarios**:

1. **Given** the repository, **When** the README is read, **Then** it contains all seven case-study sections above,
   and the stale "implements US-01 + US-05" framing is corrected to the real scope.

---

### User Story 4 - Documented cloud deployment (Priority: P2)

A documented, correct deployment process takes the app to a public URL on the cloud, with streaming working within
the platform's request limits and secrets sourced only from the secret store.

**Why this priority**: A live public link is what makes the case study shareable; the process must be documented and
correct even if not executed in this environment.

**Independent Test**: The repo contains a documented deploy process (a script / build config) targeting Cloud Run +
managed pgvector + Secret Manager; the README explains SSE behaviour within the request timeout (heartbeat) and that
all secrets come from the secret store.

**Acceptance Scenarios**:

1. **Given** a configured cloud project, **When** the documented deploy process is followed, **Then** a public URL
   serves the app, streaming works within the platform request timeout, and no secret is baked into the image or
   config — all come from the secret store.

---

### User Story 5 - Repo hygiene (Priority: P1)

The public repository has no secrets, a complete `.env.example`, a license, and CI that builds and tests every PR.

**Why this priority**: A public case-study repo that leaks a secret or won't build fails the evaluation on sight.

**Independent Test**: No secret appears in the repo or its history; `.env.example` lists every required variable; a
`LICENSE` exists; CI runs build + all test tiers on every PR.

**Acceptance Scenarios**:

1. **Given** the public repository, **When** it is inspected, **Then** there are no secrets in the tree or history,
   `.env.example` is complete, a license is present, and CI builds + runs the tests on every PR.

---

### Edge Cases

- **Cloud cold start** (scale-to-zero): the first request after idle may take a few seconds — documented, not a bug.
- **Demo key budget exhausted**: demo generation degrades to the readable "demo temporarily unavailable" message
  (US-03), the app does not look broken.
- **Local run over plain HTTP**: the secure/same-site session cookie must still work behind the local proxy (a
  documented `.env` toggle / forwarded-headers handling).
- **Missing optional secret** (no real embedding key): the app still runs (deterministic embedding stand-in); demo
  answers still stream.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST provide a **single-command local run** (`docker compose up`) that brings up the
  database (with the vector extension), applies migrations as a **step separate from app startup**, seeds the demo
  documents idempotently, and serves the app on localhost.
- **FR-002**: The frontend and API MUST be reachable from **one origin** — an nginx front serves the built frontend
  and reverse-proxies `/api` to the API, so all in-app calls (including streaming, which requires proxy buffering
  off) work without cross-origin setup.
- **FR-003**: A **`.env.example`** MUST list every required variable (database connection, the demo application key,
  the embedding key) with guidance, and MUST be the only setup a local run needs beyond filling secrets.
- **FR-004**: Uploaded files MUST persist across container restarts locally (a mounted volume), and the storage
  approach for the cloud MUST be documented.
- **FR-005**: The chat MUST show, in its empty state, **two or three suggested demo questions** matching the demo
  documents; selecting one asks it in the demo scope and streams an answer with clickable citations — with **no**
  configuration by the visitor.
- **FR-006**: The repository MUST contain a **documented cloud deployment** process (a script / build config)
  targeting a managed container platform + managed pgvector + a secret store; the README MUST cover streaming within
  the request timeout and that **all secrets come from the secret store**.
- **FR-007**: The **README** MUST be a case study containing, in order: (1) problem statement + a demo visual, (2) an
  architecture diagram, (3) design decisions each with a rejected alternative (materialized path vs CTE; pgvector
  pre-filtering; BYOK without secret persistence; grounding threshold + refusal sentinel; single embedding model;
  all-or-nothing bulk), (4) the RAG pipeline step by step, (5) the error-code catalog, (6) known limitations / future
  work, (7) run instructions (local + cloud). The stale scope framing MUST be corrected.
- **FR-008**: The repository MUST have **no secrets** in the tree or history, a **`LICENSE`**, an ignored blob-store
  directory, and **CI** that builds and runs all test tiers on every PR.
- **FR-009**: All existing behaviour MUST remain green — the four test tiers pass, and the packaging (nginx front,
  compose stack) MUST NOT change the app's runtime behaviour or the Aspire dev workflow.

### Key Entities

- **Images**: the API image (.NET publish) and the nginx image (Angular build + reverse-proxy config).
- **Compose stack**: database (pgvector) + a migration step + the API + the nginx front, with a files volume and
  `.env` config; nginx is the single public origin.
- **`.env.example`**: the documented set of required variables (secrets sourced externally).
- **Case-study README**: the primary evaluator-facing document (problem → architecture → decisions → pipeline →
  errors → limitations → run).
- **Suggested questions**: the demo empty-state prompts that start the evaluator's one-minute path.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: From a clean Docker host, filling `.env` and running one command brings the app up (migrated + seeded +
  demo working) with **no** other steps, every time.
- **SC-002**: A first-time visitor reaches a streamed, cited demo answer in **under a minute** with **zero**
  configuration (suggested question → answer + clickable citations).
- **SC-003**: The README contains **all seven** case-study sections, each design decision stating its rejected
  alternative; a reader can understand the architecture without opening the code.
- **SC-004**: The documented cloud deploy process is complete and correct (targets managed container + pgvector +
  secret store; addresses streaming timeout); **0** secrets are baked into image or config.
- **SC-005**: **0** secrets exist in the repo or its history; `.env.example` covers **100%** of required variables; a
  license is present; CI runs build + all test tiers on **100%** of PRs.
- **SC-006**: All four test tiers remain green and the Aspire dev workflow is unaffected by the static-file serving.

## Assumptions

- Local run and cloud deploy target Docker + a managed container platform (Cloud Run) + managed pgvector + a secret
  store (Secret Manager); the specific provider commands live in the README/deploy config.
- File storage stays local-disk-backed (a mounted volume locally); a cloud object-storage adapter is **future work**
  (documented), not built now.
- The demo application key and the embedding key are provided per environment via env / secret store; without the
  embedding key the deterministic stand-in is used; without the demo key, demo mode shows its unavailable message.
- The GIF/screencast is a manual capture (a placeholder/note is acceptable in the automated deliverable).
- Migrations run as a separate step (not at app startup, §VIII).

## Dependencies

- Cross-cutting over **all US-01–US-19** (19/20 merged) — this packages the finished product.

## Out of Scope

- A custom domain; monitoring / alerting; infrastructure-as-code (Terraform); a real cloud object-storage adapter
  (future work); automated GIF recording.
