# Contract — Deployment (US-20)

No API endpoints. The "contracts" here are the run interfaces and the case-study README shape.

## Local run

```bash
cp .env.example .env          # fill Anthropic__ApplicationKey + Embedding__ApiKey (both optional for a basic run)
docker compose up --build     # postgres → migrate → api → web
# open http://localhost:8080
```
- `web` (nginx) is the single public origin on `:8080`; it serves the SPA and proxies `/api` to `api:8080`.
- `migrate` applies EF migrations (incl. `CREATE EXTENSION vector`) before `api` starts; `api` seeds demo docs.
- Uploads persist in the `blobs` volume; DB in the `pgdata` volume.
- With no keys: demo mode shows "demo temporarily unavailable" (no crash); the app is fully browsable.

## Cloud (documented, `deploy/`)

- Build + push the two images; deploy the API to **Cloud Run**, the nginx front to **Cloud Run** (or the front
  proxies to the API's internal URL); **Cloud SQL** PostgreSQL with the `vector` extension; **Secret Manager** for
  `Anthropic__ApplicationKey`, `Embedding__ApiKey`, and the DB connection.
- Migrations run as a **deploy step** (the `migrate` bundle image as a Cloud Run Job / one-shot), never at app start.
- **SSE**: Cloud Run request timeout must exceed a long answer; the app's `Rag:StreamHeartbeatSeconds` keep-alive
  holds the stream open — documented, with the max-timeout note.
- **Secrets**: 0 secrets in images or config; all injected from Secret Manager. `Session__Secure=true` (HTTPS).
- **Cold start**: `min-instances=0` means the first request after idle takes a few seconds — documented, acceptable.

## README case-study shape (AC-4)

The README MUST contain, in order:
1. Problem statement + a demo visual (GIF/screenshot; a placeholder note is acceptable for the automated deliverable).
2. An architecture diagram (**mermaid**).
3. **Design decisions**, each with its rejected alternative: materialized-path folders vs recursive CTE; pgvector
   pre-filtering (session + scope) vs separate indexes; BYOK without secret persistence; grounding threshold +
   refusal sentinel; a single embedding model for the whole index; all-or-nothing bulk operations.
4. The RAG pipeline step by step (upload → extract → chunk → embed → store → retrieve → threshold → ground →
   generate → cite).
5. The error-code catalog (from `docs/features/README.md`, US-19).
6. Known limitations / future work (GCS adapter; scale-to-zero cold start; BYOK keys per-instance; monitoring; IaC).
7. Run instructions (local compose + GCP).

The stale "implements US-01 + US-05" framing is corrected to the real scope (20 stories).

## Repo hygiene (AC-5)

- No secret in the tree or history; `.env.example` complete; `LICENSE` present; `.blobstore/` ignored.
- CI builds + runs all four test tiers on every PR, plus a **docker-build** job verifying both images build.
