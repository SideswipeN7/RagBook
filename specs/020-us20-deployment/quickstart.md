# Quickstart — Validate US-20 (deployment & case study)

Prerequisites: Docker + docker-compose; .NET 10 SDK; Node 22 (for the frontend tier).

## 1. Compose stack validates + builds

```bash
docker compose config >/dev/null && echo "compose OK"     # syntax + references resolve
docker compose build                                       # API (multi-stage + migrate target) + web (nginx) images build
```
Proves: the Dockerfiles build (multi-stage, EF migration bundle target, nginx front) and the compose file is valid —
FR-001/FR-002 shape and the migration-step wiring.

## 2. One-command local run (AC-1)

```bash
cp .env.example .env      # optionally fill Anthropic__ApplicationKey + Embedding__ApiKey
docker compose up --build
# open http://localhost:8080
```
Proves: postgres → **migrate** (schema incl. `CREATE EXTENSION vector`) → **api** (demo seed) → **web** (nginx). The
app serves on localhost, the tree shows the seeded demo documents, and `GET /api/demo/status` responds. With keys,
a demo question streams; without, demo shows the unavailable message (no crash).

## 3. Frontend (AC-3)

```bash
cd src/Web && CHROME_BIN="/c/Program Files (x86)/Microsoft/Edge/Application/msedge.exe" \
  npm run test -- --watch=false --browsers=ChromeHeadless
```
Proves: the chat empty-state renders 2–3 suggested demo questions; clicking one asks it in the **demo** scope
(`store.ask` called with a demo-scope question). The four existing tiers stay green.

## 4. Docs + hygiene (AC-4 / AC-5)

- `README.md` contains the seven case-study sections (problem+visual, mermaid arch, decisions+rejected-alt, RAG
  pipeline, error-code catalog, limitations/future-work, run instructions); the stale scope framing is corrected.
- `deploy/README.md` + `deploy/cloudbuild.yaml` document the GCP path (Cloud Run + Cloud SQL pgvector + Secret
  Manager + SSE/timeout note).
- `LICENSE` exists; `.gitignore` ignores `.blobstore/`; `git log -p | grep -i` finds no secret; `.env.example` lists
  every required variable.
- CI (`.github/workflows/ci.yml`) runs the four tiers + a docker-build job on every PR.

## Acceptance mapping

| AC | Validated by |
|---|---|
| AC-1 one-command local run | §1 build · §2 up |
| AC-2 documented cloud deploy | §4 `deploy/` docs + cloudbuild |
| AC-3 evaluator < 1 min | §3 suggested questions → demo answer |
| AC-4 README case study | §4 README sections |
| AC-5 repo hygiene | §4 LICENSE / no-secrets / .env.example / CI |
