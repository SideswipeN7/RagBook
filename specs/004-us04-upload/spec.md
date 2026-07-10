# Feature Specification: Document Upload (Upload dokumentu)

**Feature Branch**: `004-us04-upload`

**Created**: 2026-07-10

**Status**: Draft

**Input**: US-04 — Upload dokumentu. A visitor uploads a PDF, TXT, or Markdown file into a chosen
folder (or the root) so they can later ask questions about its content. The upload is validated
server-side (quota → type by magic bytes → size), the binary is stored outside the relational
database via a storage abstraction, and a `Document` record is created in `Processing` state; a
message is published so background processing (US-06) can chunk and embed it. Foundation-adjacent
story (P1) in milestone M1; depends on US-01 (session), US-05 (quota), and US-09 (folders). Binding
cross-cutting decisions from `docs/features/README.md` and the constitution apply — config-driven
limits, `Result<T>` → ProblemDetails with stable codes, session isolation → 404.

## Clarifications

### Session 2026-07-10

A structured ambiguity scan was run against this spec. Most material decisions are already fixed by
US-04 "Kontekst / decyzje projektowe", the README "Decyzje przekrojowe", and the constitution, and are
therefore not re-opened —

- Accepted types are `application/pdf`, `text/plain`, `text/markdown`, validated by **file signature
  (magic bytes)**, not the extension or the client-declared content type — fixed by the story.
- The upload is synchronous only up to storing the file and creating a `Document(Status=Processing)`;
  **chunking and embeddings are US-06** — fixed by the story.
- Binary content lives **outside the relational database** behind a storage abstraction (local volume
  today, cloud object storage in production) — fixed by the story.
- The per-file size limit is the **existing US-05 quota** per-file maximum (config-driven, no magic
  numbers); the document-count and total-size limits are the same US-05 quota, admitted **atomically** —
  fixed by the story + US-05.
- Errors flow through `Result<T>` → ProblemDetails with a stable `code`; a folder id owned by another
  session reads as not-found (404) — fixed by README + constitution §II/§III.

Three ambiguities were resolved by product input this session:

- Q: TXT and MD have no magic-byte signature — how are text uploads validated and classified? → A:
  **The signature check rejects binary content; a non-PDF upload must be valid UTF-8 text (no NUL / no
  disallowed control bytes) to be accepted, and is classified by extension — `.md` → `text/markdown`,
  otherwise `text/plain`.** PDFs are still identified by the `%PDF-` signature.
- Q: How is the duplicate-name auto-suffix kept collision-free under concurrency? → A: **A unique index
  on `(folder_id, LOWER(file_name))` is the authority; on a unique violation the handler increments the
  suffix and retries** (mirrors the folder uniqueness approach), so two concurrent same-name uploads
  cannot both land the same name.
- Q: What is the auto-suffix format and starting number? → A: **`name (n).ext` with a space before the
  parenthesis, `n` starting at 1** — the first duplicate becomes `umowa (1).pdf`, the next
  `umowa (2).pdf`, and so on to the first free integer.

Implementation-level decisions are deferred to `plan.md` (not product input): the storage-path layout
and the storage abstraction's local/cloud drivers, the exact PDF signature bytes, the multipart
buffering strategy, and the exact ordering of validate-vs-store within the handler.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Upload a valid file into a folder or the root (Priority: P1)

A visitor with free quota selects a target folder (or none, meaning the root) and uploads a supported
file. The system accepts it, stores the content, records the document as *processing*, and the file
appears immediately in the tree with a processing indicator.

**Why this priority**: This is the core of the story — getting a document into the workspace is the
prerequisite for every later capability (tree listing, deletion, and ultimately RAG questions).

**Independent Test**: Upload a valid small PDF to a chosen folder and observe a success response
carrying the new document (in *processing* state) placed under that folder; upload with no folder and
observe it placed at the root.

**Acceptance Scenarios**:

1. **Given** a session with free quota and a chosen target folder, **When** the visitor uploads a valid
   PDF within the size limit, **Then** the system responds success with the new document in
   *processing* state and the document is associated with that folder.
2. **Given** no folder is chosen, **When** the visitor uploads a valid file, **Then** the document is
   created at the root (no folder).
3. **Given** the upload succeeded, **When** the tree is next read, **Then** the document appears under
   its folder with a processing indicator.

---

### User Story 2 - Reject an unsupported file type by its signature (Priority: P1)

A visitor tries to upload a file whose real content is not a supported type — for example an
executable renamed to `.pdf`. The system inspects the file's actual signature, refuses it, and names
the allowed formats.

**Why this priority**: Trusting the extension or the declared content type is a security and
data-quality hole; signature validation is the guarantee that only real PDF/TXT/MD content enters the
index.

**Independent Test**: Upload a non-PDF payload with a `.pdf` name and observe a stable
"unsupported file type" failure that lists the allowed formats, with nothing stored or recorded.

**Acceptance Scenarios**:

1. **Given** a file whose bytes are not a supported type (e.g. an `.exe` renamed to `.pdf`), **When**
   the visitor uploads it, **Then** the system rejects it with a stable "unsupported file type" error
   code and a message listing the allowed formats, and nothing is stored or recorded.
2. **Given** a genuine TXT or Markdown file, **When** it is uploaded, **Then** it is accepted (the
   signature check admits the supported text types).

---

### User Story 3 - Reject oversized and empty files server-side (Priority: P1)

A visitor tries to upload a file larger than the allowed maximum, or an empty (0-byte) file. The
system refuses both before storing anything, regardless of any client-side pre-check.

**Why this priority**: The client may pre-validate for a better experience, but the server is the
source of truth; an oversized or empty file must never reach storage or the index.

**Independent Test**: Upload a file exceeding the configured per-file maximum and observe a
"file too large" failure; upload a 0-byte file and observe an "empty file" failure — both with nothing
stored.

**Acceptance Scenarios**:

1. **Given** a file larger than the configured per-file maximum, **When** the visitor uploads it,
   **Then** the system rejects it with a stable "file too large" error code before storing anything.
2. **Given** a 0-byte file, **When** the visitor uploads it, **Then** the system rejects it with a
   stable "empty file" error code before storing anything.

---

### User Story 4 - Duplicate filename gets an auto-suffix (Priority: P1)

A visitor uploads a file whose name already exists in the target folder. The system accepts it and
gives it an auto-suffixed name (e.g. `umowa (2).pdf`) — it never blocks the upload and never overwrites
the existing file.

**Why this priority**: Users routinely upload same-named files; silently blocking or overwriting would
surprise them or lose data. A predictable suffix keeps both files.

**Independent Test**: Upload `umowa.pdf` twice into the same folder and observe the second stored as
`umowa (2).pdf`, both documents present.

**Acceptance Scenarios**:

1. **Given** a folder already containing `umowa.pdf`, **When** the visitor uploads another `umowa.pdf`
   to the same folder, **Then** it is accepted and named `umowa (1).pdf`; the original is unchanged.
2. **Given** `umowa.pdf` and `umowa (1).pdf` already exist, **When** a third `umowa.pdf` is uploaded,
   **Then** it becomes `umowa (2).pdf` (the suffix increments to the first free number).
3. **Given** the same filename exists in a *different* folder, **When** a file of that name is
   uploaded, **Then** no suffix is applied — the uniqueness is per folder.

---

### User Story 5 - Quota is enforced atomically at upload (Priority: P1)

A visitor at (or near) their quota tries to upload. The system admits the document only if it keeps the
session within the file-count and total-size limits, and does so atomically so two concurrent uploads
at the boundary cannot both slip through.

**Why this priority**: US-05 built the quota and its atomic admit precisely so the real upload would
enforce it; a bypass here would make the whole quota advisory.

**Independent Test**: With a session at the document-count limit, attempt an upload and observe a
"quota exceeded" failure with nothing stored; with two concurrent uploads at one-below-limit, observe
at most one admitted.

**Acceptance Scenarios**:

1. **Given** a session at the document-count limit, **When** the visitor uploads a file, **Then** the
   system rejects it with the stable "quota exceeded" code before storing anything.
2. **Given** a session whose remaining storage is smaller than the file, **When** the visitor uploads
   it, **Then** the system rejects it with the "total size quota exceeded" code before storing anything.
3. **Given** a session one below the file-count limit, **When** two uploads arrive concurrently,
   **Then** at most one is admitted (the quota admit is atomic).

---

### Edge Cases

- **Encrypted or corrupt PDF** → the signature check passes (it is a real PDF), so the upload is
  accepted and recorded as *processing*; the *content* problem surfaces later as a failed processing
  state (US-06), not at upload time.
- **Interrupted upload** → if the transfer or the file store fails, **no document record and no stored
  file remain**: the record is created only after the content is durably stored, and a partially
  stored file left by a failure is cleaned up (no orphans).
- **Cross-session target folder** → uploading into a `folderId` owned by another session behaves as if
  the folder does not exist (404), never revealing it.
- **Quota admit succeeds but storage fails** → the admitted document is not left counting against the
  quota; the operation is unwound so the session's usage is unchanged.
- **Filename with no base name or only an extension** (e.g. `.gitignore`) → stored as given; the suffix
  scheme still applies on collision within the folder.
- **Binary payload with a text extension** (e.g. a `.txt` that is actually a compiled binary) → the
  UTF-8 validity check fails, so it is rejected as an unsupported type, not stored as text.
- **Concurrent duplicate uploads** → two same-name uploads into one folder race; the unique index on
  `(folder_id, LOWER(file_name))` admits distinct names (one keeps the base name or `(1)`, the other
  retries to the next free suffix) — never two identical names.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Users MUST be able to upload a single file (PDF, TXT, or Markdown) into a chosen folder
  they own, or — when no folder is chosen — into the root. The stored document is scoped to the current
  session.
- **FR-002**: The system MUST validate the file **type by its content**, not by the extension or the
  client-declared content type. A PDF is identified by its `%PDF-` signature; a non-PDF upload MUST be
  **valid UTF-8 text** (no NUL / disallowed control bytes) to be accepted and is classified by
  extension — `.md` → `text/markdown`, otherwise `text/plain`. Any upload that is neither a real PDF nor
  valid text MUST be rejected with a stable "unsupported file type" code that names the allowed formats,
  storing and recording nothing.
- **FR-003**: The system MUST enforce, server-side, that the file does not exceed the configured
  per-file maximum size (the US-05 quota per-file limit, config-driven), rejecting an oversized file
  with a stable "file too large" code before storing anything — regardless of any client pre-check.
- **FR-004**: The system MUST reject a 0-byte (empty) file with a stable "empty file" code before
  storing anything.
- **FR-005**: An accepted document MUST be associated with the chosen folder (its `folder_id`) or with
  the root (no folder) when none is chosen.
- **FR-006**: A chosen folder id that belongs to another session MUST behave as not-found (404), never
  disclosing its existence (isolation inherited from US-01/US-09), and nothing MUST be stored.
- **FR-007**: The system MUST enforce the session quota (document count and total storage) **atomically**
  at upload via the existing US-05 admit, so an upload that would breach either limit is rejected with
  the matching stable "quota exceeded" / "total size quota exceeded" code before storing anything, and
  two concurrent uploads at the boundary admit at most one.
- **FR-008**: When the target folder already contains a document with the same name (compared
  case-insensitively), the system MUST accept the upload and assign an **auto-suffixed** name
  (`name (n).ext`, space before the parenthesis, `n` the first free integer starting at **1**), never
  blocking and never overwriting. Suffixing is scoped per folder. Uniqueness MUST be guaranteed under
  concurrency by a unique index on `(folder_id, LOWER(file_name))`: on a unique violation the system
  increments the suffix and retries, so two concurrent same-name uploads cannot both take the same name.
- **FR-009**: The binary content MUST be stored **outside the relational database** through a storage
  abstraction, so the same upload path works against local storage in development and cloud object
  storage in production without code changes.
- **FR-010**: On success the system MUST create a `Document` record in **`Processing`** state carrying
  at least: owning session, folder association, file name (post-suffix), content type, size in bytes, a
  pointer to the stored content, an upload timestamp, and an initial chunk count of zero. The stored
  content type MUST be the **detected/classified** canonical type (`application/pdf`, `text/markdown`,
  or `text/plain`), never the client-declared value.
- **FR-011**: On success the system MUST publish a **"document uploaded"** message carrying the document
  id, so background processing (US-06) can pick it up. US-04 does not perform chunking or embedding.
- **FR-012**: The record MUST be created only **after** the content is durably stored; if storage or
  persistence fails, the operation MUST leave **no orphaned record and no orphaned stored file**
  (partial content is cleaned up), and the session's quota usage MUST be unchanged.
- **FR-013**: All upload failures MUST be returned through the standard `Result` → ProblemDetails
  channel with a stable, machine-readable `code`, never as a naked 500.
- **FR-014**: The document–folder association MUST make the folder "contains files" check real, so that
  deleting a folder that holds documents is blocked (completing US-09 AC-5, whose file arm was a
  forward-looking seam until this story).
- **FR-015**: The frontend MUST provide an upload affordance (a button and drag-and-drop of a file onto
  the tree) with client-side pre-validation of type and size (a convenience, not the authority), a
  progress indication during transfer, and MUST show the new document under its folder in a processing
  state; it MUST surface each error code as a human-readable message.

### Key Entities *(include if feature involves data)*

- **Document (extended)**: The session-owned file record introduced minimally by US-05 (id, session,
  size, status, origin), now extended with its folder association, file name, content type, a pointer to
  the stored binary, an upload timestamp, and a chunk count. Its lifecycle status begins at *processing*
  at upload; later transitions (ready/failed) are owned by US-06.
- **Stored file (binary)**: The uploaded content held by the storage abstraction outside the relational
  database, addressed by the document's stored-content pointer. Its lifecycle is tied to the document —
  created before the record, removed if the upload is unwound.
- **"Document uploaded" event**: An in-process message carrying the new document id, published on a
  successful upload so background processing can begin; it is the seam between US-04 and US-06.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A valid supported file uploaded to a chosen folder is accepted and appears in the tree in
  a processing state under that folder in 100% of valid uploads; with no folder chosen it appears at the
  root.
- **SC-002**: 100% of files whose signature is not a supported type are rejected with the
  unsupported-type code — including a mismatched extension (e.g. an executable named `.pdf`) — with
  nothing stored.
- **SC-003**: 100% of oversized files and 100% of 0-byte files are rejected server-side before anything
  is stored, each with its correct distinct code, even when the client pre-check is bypassed.
- **SC-004**: Uploading a duplicate name into the same folder yields a suffixed name starting at
  `name (1).ext` and never overwrites; the same name in a different folder is stored unsuffixed; two
  concurrent same-name uploads yield two distinct names (no collision) — in 100% of cases.
- **SC-005**: 100% of uploads that would breach the file-count or total-size quota are rejected before
  storing anything; under two concurrent uploads at one-below-limit, at most one is admitted (0%
  over-admit across repeated runs).
- **SC-006**: After a failed storage or persistence step, 0 orphaned records and 0 orphaned stored files
  remain, and the session's quota usage is unchanged.
- **SC-007**: After US-04, deleting a folder that contains at least one document is blocked (the US-09
  emptiness rule now covers files), in 100% of cases.
- **SC-008**: A document uploaded by one session is never visible to, or countable by, another session.

## Assumptions

- **US-01, US-05, US-09 are in place**: session identity + isolation, the quota mechanism and its atomic
  admit seam, and the folder tree (with `folder_id` target and the forward-looking file-probe seam)
  already exist and are reused; US-04 wires through them rather than re-implementing them.
- **Background processing (US-06) is not built here**: US-04 stores the file, records the document as
  *processing*, and publishes the "uploaded" event; chunking, embeddings, and the ready/failed
  transitions are US-06.
- **Single-file upload only**: multi-file / bulk upload is out of scope (bulk operations on existing
  documents are US-12); each request uploads one file.
- **Supported types are fixed**: PDF, TXT, Markdown only; OCR of scans, DOCX, and images are out of
  scope.
- **Storage abstraction**: a local driver (mounted volume) is used in development and a cloud
  object-storage driver in production, behind one interface; the exact drivers and path layout are a
  planning decision.
- **The per-file, count, and total-size limits are the US-05 quota** (config-driven); US-04 introduces
  no new numeric limits.
- **Duplicate-name suffixing is per folder** and increments to the first free integer starting at 1
  (`name (1).ext`, `name (2).ext`, …); filename comparison for collision is case-insensitive, backed by
  a unique index on `(folder_id, LOWER(file_name))`, following the same convention as folder names.
