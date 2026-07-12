# Quickstart — Validate US-06

## Prerequisites

- .NET 10 SDK, Node.js (Angular 20), Docker running (Testcontainers `pgvector/pgvector:pg17`).
- `dotnet tool restore` before creating/applying migrations.
- **No embedding key needed** in dev/tests — the deterministic `FakeEmbeddingProvider` is used when
  `Embedding:ApiKey` is unset. Set `Embedding:ApiKey` (Secret Manager) to exercise the real Voyage driver.

## Run locally (Aspire)

```sh
cd src/Web && npm install && cd -
dotnet run --project src/RagBook.AppHost
# Upload a readable PDF/TXT/MD (US-04). Its row shows "Przetwarzanie…" then flips to "Gotowe · N fragm."
# on its own (SSE push) — no reload. Upload a scan-only/encrypted PDF → it flips to "Błąd" with a reason.
```

## Automated validation (source of truth for DoD)

```sh
# Cheapest tiers (no Docker)
dotnet test tests/RagBook.Domain.Tests        # StructuralChunker (size/overlap/short), Document transitions, FakeEmbeddingProvider (deterministic, dimension)
dotnet test tests/RagBook.Application.Tests    # ProcessDocumentHandler with fakes — all 5 ACs

# Integration tier — START DOCKER FIRST (Testcontainers pgvector)
dotnet test tests/RagBook.Api.IntegrationTests # end-to-end index, cascade delete, session isolation

# Frontend
cd src/Web && npm test                         # DocumentStatusStore applies an SSE update to TreeStore
```

Tests map to acceptance criteria:

| AC | Tier | Test | Proves |
|---|---|---|---|
| AC-1 | Application | `Should_ExtractChunkEmbedAndMarkReady_When_Readable` | happy path → chunks+vectors, Ready, chunk count, status published |
| AC-1 | Integration | `Should_IndexDocument_EndToEnd` | real TXT/PDF → chunks with vectors persisted; document Ready |
| AC-2 | Application | `Should_MarkFailed_When_NoExtractableText` | unreadable → Failed + reason, zero chunks |
| AC-3 | Application | `Should_MarkFailedWithProviderError_And_LeaveNoChunks_When_ProviderKeepsFailing` | exhausted retries → Failed(provider), no partial index |
| AC-3 | Application | `Should_Succeed_When_ProviderFailsThenRecovers` | transient error tolerated (retry) |
| AC-4 | Application | `Should_ProduceIdenticalChunks_When_ProcessedTwice` | redelivery → no duplicate chunks (replace) |
| AC-4 | Application | `Should_StopQuietly_When_DocumentDeleted` | missing target → no-op |
| AC-5 | Application | `Should_BatchEmbeddingCalls_When_ManyChunks` | 200 chunks / 64 → 4 provider calls (not 200) |
| FR-005 | Domain | `Should_ProduceSingleChunkNoOverlap_When_ShortText` / `Should_OverlapAndKeepPageNumbers` | chunking rules |
| FR-009 | Integration | `Should_CascadeDeleteChunks_When_DocumentDeleted` | chunks removed with the document (US-08 relies on it) |
| FR-014 | Integration | `Should_NotExposeChunksToAnotherSession` | session isolation |
| FR-016 | Web | `DocumentStatusStore updates the tree on an SSE status event` | push → tree flips without reload |

## Manual smoke (optional)

```sh
# subscribe to the status stream for the current session
curl -N -c jar -b jar https://localhost:<api>/api/documents/status/stream
# in another terminal upload a file → observe a `data: {"status":"Ready",...}` line appear
```

## Expected outcomes

- A readable upload is indexed on its own (chunks + vectors, Ready, chunk count) and the tree flips via SSE.
- An unreadable file ends Failed with a readable reason; no chunks.
- Provider blips retry; a permanent failure ends Failed(provider) with **no** partial chunks.
- Reprocessing the same document yields no duplicate chunks; a deleted document is skipped quietly.
- Embeddings are batched; chunks cascade-delete with the document; chunks never cross sessions.
