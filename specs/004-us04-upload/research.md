# Phase 0 Research — Document Upload (US-04)

All items below were fixed by the spec/clarify/constitution (recorded for traceability) or are
implementation choices resolved here. No open `NEEDS CLARIFICATION` remain.

## D1 — Content-based type detection

- **Decision**: `FileTypeDetector.Detect(bytes, fileName)` returns a `SupportedFileType` or "unsupported":
  a leading **`%PDF-`** signature → `application/pdf`; otherwise the content must be **valid UTF-8 text**
  (decodes without replacement and contains no NUL / disallowed control bytes except tab/CR/LF), and is
  classified by extension — `.md`/`.markdown` → `text/markdown`, else → `text/plain`. Anything else →
  `document.unsupported_file_type` (message lists PDF/TXT/MD).
- **Rationale**: PDFs have a real signature; TXT and MD do **not** (both are plain text), so "magic
  bytes" for text means "prove it is text, not binary" + classify by name. This rejects an `.exe`
  renamed `.pdf` (no `%PDF-`, not valid text) and a binary renamed `.txt` (fails UTF-8), satisfying AC-2.
- **Alternatives**: trusting the declared content type (rejected — the story forbids it, security hole);
  a full MIME-sniffing library (overkill for three types); parsing the PDF structure (US-06's job).

## D2 — Validation order & where it runs

- **Decision**: In the handler: **(1)** reject empty (0 bytes → `document.empty_file`); **(2)** detect
  type (D1); **(3)** reject over the per-file size limit (`QuotaOptions.MaxFileSizeMb` → `quota.file_too_large`);
  **(4)** resolve/authorize the target folder; **(5)** store the blob; **(6)** atomically admit + insert
  (quota count/total, D5). Steps 1–3 need only the buffered bytes and run **before** any storage.
- **Rationale**: The cheapest, side-effect-free checks first; nothing is stored until type+size+empty
  pass and the folder is valid. Size uses the existing US-05 config limit — no new magic number.
- **Note**: The story lists "quota → type → size", but the atomic **count/total** quota admit must wrap
  the insert (D5), so the *per-file size* guard runs early (cheap, pre-store) and the *count/total* quota
  is enforced atomically at insert. Both are the US-05 `QuotaOptions`.

## D3 — `IFileStorage` abstraction + local driver

- **Decision**: `IFileStorage` in Core Domain: `Task<string> SaveAsync(Stream, string suggestedName, ct)`
  → an opaque **storage path/key**, `Task<Stream> OpenReadAsync(string path, ct)`, `Task DeleteAsync(string
  path, ct)`. `LocalFileStorage` (Infrastructure) writes under a **config-driven root**
  (`FileStorageOptions.RootPath`), namespacing by session id and a generated blob id
  (`{root}/{sessionId}/{guid}{ext}`) so names never collide on disk and listing a session's blobs is
  cheap. Production swaps a cloud-object-storage driver behind the same interface (out of scope here).
- **Rationale**: Keeps binaries out of Postgres (constitution/story), one seam for local+cloud, and the
  on-disk name is independent of the user-facing `file_name` (which is deduplicated separately, D6).
- **Alternatives**: bytea-in-DB (rejected by the story); coupling the storage key to the display name
  (fragile — rename/suffix would move blobs).

## D4 — Store-then-record with orphan cleanup

- **Decision**: The blob is written **before** the `Document` row. If the subsequent admit/insert fails
  (quota, unique-retry exhaustion, or any error), the handler **deletes the just-stored blob** in a
  `finally`/compensation path, so no orphan file and no orphan row remain (FR-012). The row is the
  commit point; a stored-but-unrecorded blob is always cleaned up.
- **Rationale**: A DB row without its blob is unusable; a blob without a row is invisible and leaks.
  Storing first lets the row carry a valid `storage_path`; compensation covers the failure window. Object
  storage has no cross-transaction with Postgres, so compensation is the pragmatic MVP guarantee.
- **Alternatives**: record-then-store (row would point at a missing blob on storage failure); a 2-phase/
  saga (overkill at case-study scale).

## D5 — Atomic quota admit reused; free suffix computed under the lock (no in-transaction retry)

- **Decision**: Add `IDocumentUploadRepository.AddUploadedWithinQuotaAsync(Document, QuotaLimits, ct)`
  in the Documents module, implemented beside `DocumentQuotaRepository`: it opens the **same
  transaction-scoped `pg_advisory_xact_lock(sessionKey)`** as US-05. **Because the advisory lock is
  per-session, all of a session's uploads are serialized**, so under the lock the repository (1) re-reads
  usage and evaluates `QuotaSnapshot.CanAdmit` (quota failure → quota code, no insert), (2) **computes
  the first free file name** in the target folder — reads the existing `LOWER(file_name)` set for that
  `(session, folder)` and picks the base name or the lowest free `name (n)` (n from 1) via
  `Document.RenameForSuffix` — and (3) **inserts once**. The two partial unique file-name indexes remain
  as a **backstop** (defence in depth), not the primary collision path.
- **Rationale**: A same-transaction "insert → catch 23505 → retry insert" loop is **wrong on
  PostgreSQL**: a constraint violation aborts the transaction, so a subsequent statement fails with
  *current transaction is aborted* unless each attempt is wrapped in a `SAVEPOINT`. It is also
  unnecessary here: the per-session advisory lock already serializes the session's uploads, so the
  free-suffix computed under the lock is reliable and the insert cannot collide with another upload of
  the same session. This keeps one clean insert, reuses the US-05 atomicity, and leaves
  `TryAddWithinQuotaAsync` untouched (US-05 tests stay green). *(Superseded HIGH finding P1 from
  `/speckit-analyze`: the earlier "retry-on-23505 in the same transaction" plan.)*
- **Alternatives**: `SAVEPOINT` per insert attempt (works, but more moving parts than computing the free
  name under a lock that already serializes); retry loop in the handler calling US-05's method (can't
  distinguish name vs quota conflict; re-locks repeatedly); a DB sequence per name (complex).

## D6 — Duplicate-name suffix (`FileName` value object)

- **Decision**: `FileName` splits a name into `base` + `extension`; `WithSuffix(n)` →
  `"{base} ({n}){ext}"`. On collision the suffix starts at **1** and increments to the first free integer
  (clarify Q3). The handler seeds an initial candidate (best-effort query of existing names in the
  folder), and the unique index + retry (D5) is the authority under concurrency. Comparison is
  **case-insensitive** (`LOWER(file_name)`), per folder.
- **Rationale**: Matches the clarified format; the value object keeps parsing/formatting pure and tested
  (AC-5). The index guarantees no two identical names even under a race.

## D7 — File-name uniqueness indexes

- **Decision**: Two **partial unique indexes**, both `WHERE file_name IS NOT NULL`:
  - root: `UNIQUE (user_session_id, LOWER(file_name)) WHERE folder_id IS NULL AND file_name IS NOT NULL`
  - foldered: `UNIQUE (folder_id, LOWER(file_name)) WHERE folder_id IS NOT NULL AND file_name IS NOT NULL`
- **Rationale**: Per-folder uniqueness with a nullable `folder_id` (root) is the same NULL-distinct issue
  as folders → two partial indexes. `file_name IS NOT NULL` excludes US-05's minimal seed documents (no
  file), so they never conflict. `LOWER` gives the case-insensitive rule (D6) without `citext`.
- **Alternatives**: single composite unique (misses root duplicates, breaks on file-less rows).

## D8 — `DocumentUploaded` event & closing US-09's file arm

- **Decision**: On a committed upload the handler **publishes `DocumentUploaded(DocumentId)`** as an
  in-process Wolverine **`IEvent`**; US-06 will subscribe. US-04 ships **no** subscriber. Separately,
  `NoFolderFilesProbe` is **replaced** by `DocumentFolderFileProbe` implementing
  `IFolderFileProbe.HasFilesAsync(folderId)` = `EXISTS(documents WHERE folder_id = folderId)` — so US-09's
  delete now blocks folders that contain files (FR-014 / US-09 AC-5 closed).
- **Rationale**: The event is the clean US-04→US-06 seam (no direct dependency, constitution §I). The
  probe swap is the single integration point US-09 designed for — no change to the folder delete handler.
- **Alternatives**: a durable `IExternalEvent`/outbox (unnecessary — processing is in-process, US-06);
  querying files from the Folders module directly (would couple modules — the probe seam avoids it).
