# Deploying RagBook to Google Cloud

This documents a Cloud Run deployment (the same images `docker compose` builds locally). It is a reference process —
adapt project ids / regions to your account. Secrets come **only** from Secret Manager; nothing is baked into an
image or a config file.

## Topology

```
Browser ─HTTPS─▶ Cloud Run: web (nginx)  ──/api──▶ Cloud Run: api (.NET)  ──▶ Cloud SQL (PostgreSQL + pgvector)
                     serves the SPA          (internal)                        files: mounted volume (see limits)
Secret Manager ─▶ api:  Anthropic__ApplicationKey, Embedding__ApiKey, ConnectionStrings__ragbookdb
```

The **nginx** service is the public origin; it serves the Angular build and reverse-proxies `/api` to the internal
API service (same-origin, no CORS). Streaming works because nginx has `proxy_buffering off` and the app sends an SSE
keep-alive every `Rag:StreamHeartbeatSeconds` (15s) — set the Cloud Run **request timeout** above your longest answer
(e.g. 300s).

## One-time setup

```bash
PROJECT=your-gcp-project ; REGION=europe-central2 ; REPO=ragbook
gcloud config set project "$PROJECT"
gcloud services enable run.googleapis.com sqladmin.googleapis.com secretmanager.googleapis.com artifactregistry.googleapis.com cloudbuild.googleapis.com
gcloud artifacts repositories create "$REPO" --repository-format=docker --location="$REGION"

# Cloud SQL PostgreSQL (pgvector is available on Cloud SQL for PostgreSQL 15+; enable the extension in the DB).
gcloud sql instances create ragbook-db --database-version=POSTGRES_17 --region="$REGION" --tier=db-f1-micro
gcloud sql databases create ragbookdb --instance=ragbook-db
gcloud sql users set-password postgres --instance=ragbook-db --password="<db-password>"
# In the DB (via `gcloud sql connect ragbook-db`): CREATE EXTENSION IF NOT EXISTS vector;  (the migration also does this)

# Secrets
printf '%s' '<anthropic-app-key>' | gcloud secrets create anthropic-app-key --data-file=-
printf '%s' '<voyage-embedding-key>' | gcloud secrets create embedding-key --data-file=-
printf '%s' 'Host=/cloudsql/PROJECT:REGION:ragbook-db;Database=ragbookdb;Username=postgres;Password=<db-password>' \
  | gcloud secrets create ragbookdb-conn --data-file=-
```

## Build + push images

```bash
IMG=$REGION-docker.pkg.dev/$PROJECT/$REPO
docker build -f src/RagBook.API/Dockerfile --target api     -t $IMG/api:latest .
docker build -f src/RagBook.API/Dockerfile --target migrate -t $IMG/migrate:latest .
docker build -f src/Web/Dockerfile -t $IMG/web:latest src/Web
docker push $IMG/api:latest ; docker push $IMG/migrate:latest ; docker push $IMG/web:latest
# or: gcloud builds submit --config deploy/cloudbuild.yaml
```

## Migrate (a deploy step — never at app startup, constitution §VIII)

Run the migration bundle once as a Cloud Run **Job** (or locally against the instance) before/with each deploy:

```bash
# The connection is injected as the MIGRATE_CONN secret env var; a shell wrapper expands it into --connection
# (Cloud Run does not expand env references inside --args, so run efbundle under /bin/sh).
gcloud run jobs deploy ragbook-migrate --image $IMG/migrate:latest --region "$REGION" \
  --set-cloudsql-instances "$PROJECT:$REGION:ragbook-db" \
  --set-secrets "MIGRATE_CONN=ragbookdb-conn:latest" \
  --command "/bin/sh" --args '-c,./efbundle --connection "$MIGRATE_CONN"'
gcloud run jobs execute ragbook-migrate --region "$REGION" --wait
```

## Deploy the services

```bash
# API (internal — only the web front calls it)
gcloud run deploy ragbook-api --image $IMG/api:latest --region "$REGION" --no-allow-unauthenticated \
  --set-cloudsql-instances "$PROJECT:$REGION:ragbook-db" \
  --set-secrets "ConnectionStrings__ragbookdb=ragbookdb-conn:latest,Anthropic__ApplicationKey=anthropic-app-key:latest,Embedding__ApiKey=embedding-key:latest" \
  --set-env-vars "Session__Secure=true,Wolverine__DurabilityEnabled=true" --timeout 300

# Web (public) — nginx serving the SPA + proxying /api to the API's URL.
gcloud run deploy ragbook-web --image $IMG/web:latest --region "$REGION" --allow-unauthenticated --timeout 300
```

> The provided `nginx.conf` proxies to `http://api:8080` (the compose service name). For Cloud Run, either put both
> services behind one domain/load-balancer, or template the API URL into `nginx.conf` at build/deploy time. File
> storage uses `LocalFileStorage` on the instance's ephemeral disk — for durable uploads across instances, add a GCS
> `IFileStorage` adapter (see the README "Future work").

## Notes

- **Secrets**: only from Secret Manager; `Session__Secure=true` over HTTPS.
- **Cold start**: `min-instances=0` (default) means the first request after idle takes a few seconds — acceptable.
- **Demo budget exhausted**: demo answers degrade to a readable "demo temporarily unavailable" message (US-03), not a
  broken UI.
