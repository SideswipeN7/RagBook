# Feature Specification: Notebook-style workspace redesign (US-21)

**Feature Branch**: `021-us21-workspace`

**Created**: 2026-07-18

**Status**: Draft

**Input**: User request: reshape the app into a NotebookLM-style workspace — config first, then a multi-column layout
with a collapsible **conversations** list, a **sources** column (documents pinned to the active conversation, with
drag-drop into folders + multi-select), the **chat** in the middle, and a **Studio** column with a real AI
visualization. "Flow powinno być inne."

## Context

RagBook is feature-complete (20/20) but its shell is a **single stacked column** (quota → API key → upload → tree →
chat). The user wants the information architecture reorganised around **conversations that own their sources** (like
NotebookLM notebooks): the visitor configures access first, then works in a 4-column workspace —
**conversations | sources | chat | Studio**. This is a real change: documents gain a link to a conversation
(a domain change + migration), retrieval/upload/tree become conversation-scoped, a shared active-conversation state
drives all columns, an onboarding gate precedes the workspace, and a new Studio panel generates an AI **summary** of
the active conversation's sources. Session isolation (§III), BYOK/demo keys, folders + drag-drop + multi-select, and
the RAG pipeline are all reused; the change is the shell + the conversation↔sources link + the summary.

Delivery is **staged** across PRs so each stays green + reviewable: (1) the 4-column shell + onboarding + shared
active state (frontend); (2) sources pinned per conversation (domain + migration + upload + tree + retrieval); (3)
the Studio summary.

## Clarifications

### Session 2026-07-18

- Q: How should folders coexist with per-conversation sources (keeping folders + drag-drop + multi-select)? → A:
  **Per-conversation folders** — both `Folder` and `Document` gain a `ConversationId`; each conversation has its own
  folder tree + sources; drag-drop and multi-select work within a conversation; a new conversation starts with an
  empty tree. The retrieval folder-scope (`path LIKE`) is additionally filtered by conversation, and the `all` scope
  means "all of this conversation's ready sources". A migration adds `conversation_id` to `documents` **and**
  `folders`.
- Q: Behaviour for legacy (unpinned) documents, demo documents, and deleting a conversation? → A: **Clean start +
  cascade** — the project has no production data, so documents/folders with a null conversation (legacy) do **not**
  appear in the new per-conversation view (clean start); demo documents stay **global, read-only** (keyless demo
  works in any conversation); deleting a conversation **cascades** the delete of its folders, sources, and chunks
  (like a NotebookLM notebook).
- Q: The first working Studio visualization? → A: a **summary** of the active conversation's sources (an AI overview
  reusing the generation + retrieval stack); other Studio tiles are upcoming placeholders.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Configure first, then a 4-column workspace (Priority: P1) 🎯 MVP

On first visit the user is guided to configure access (their own API key, or start in demo) before the workspace
appears; the workspace is a 4-column layout — a collapsible conversations list, a sources column, the chat, and a
Studio column.

**Why this priority**: The new information architecture is the whole request; every other story lives inside it.

**Independent Test**: A first-time visitor sees a config step; after configuring (key set) or choosing demo, the
4-column workspace renders with the conversations list (collapsible), the sources column, the chat, and Studio; all
four react to the active conversation.

**Acceptance Scenarios**:

1. **Given** a fresh visit with no key, **When** the app loads, **Then** a config-first step is shown (enter an API
   key, or continue in demo); the full workspace is not shown until access is configured or demo is chosen.
2. **Given** access is configured, **When** the workspace renders, **Then** it shows four regions —
   conversations (collapsible) | sources | chat | Studio — and selecting a conversation updates all of them from one
   shared active-conversation state.

---

### User Story 2 - Sources belong to the active conversation (Priority: P1)

The sources column shows the documents pinned to the **active conversation**; uploading a document adds it to that
conversation; the chat answers from that conversation's sources; drag-drop into folders and multi-select still work.

**Why this priority**: "Documents for a given conversation" is the core model change that makes the workspace a
notebook.

**Independent Test**: In conversation A, upload a document → it appears in A's sources and not in a new conversation
B; asking in A grounds only on A's sources; dragging a source into a folder and multi-selecting still work.

**Acceptance Scenarios**:

1. **Given** an active conversation, **When** the user uploads a document, **Then** it is pinned to that conversation
   and appears in its sources column (not in other conversations).
2. **Given** a conversation with sources, **When** the user asks a question, **Then** the answer is grounded only on
   that conversation's sources (retrieval is conversation-scoped).
3. **Given** the sources column, **When** the user drags a source into a folder or multi-selects several, **Then**
   the existing move / bulk operations work unchanged within the conversation.

---

### User Story 3 - Studio: an AI summary of the sources (Priority: P2)

The Studio column offers a real visualization — an AI-generated **summary** of the active conversation's sources —
plus placeholders for future visualizations.

**Why this priority**: Delivers a working Studio pane (not just a shell), the concrete "visualization" the user asked
for, reusing the generation + retrieval stack.

**Independent Test**: With sources in a conversation, click "Podsumowanie" → a generated summary of those sources
appears (on the BYOK or demo key); with no sources, a clear empty/disabled state; other Studio tiles show "wkrótce".

**Acceptance Scenarios**:

1. **Given** an active conversation with ready sources and a configured key (or demo), **When** the user requests a
   summary, **Then** a generated summary of that conversation's sources is shown.
2. **Given** a conversation with no ready sources, **When** the Studio summary is opened, **Then** a clear
   empty/disabled state is shown (no error), and the remaining Studio tiles are marked as upcoming.

---

### Edge Cases

- **Legacy / unpinned documents**: documents that predate the conversation link (null conversation) and global demo
  documents — how they surface in the new sources model (see Clarifications).
- **Deleting a conversation**: what happens to its pinned sources (kept as unassigned vs deleted — see Clarifications).
- **No key and no demo configured**: the config-first gate must still allow browsing the workspace read-only, and
  clearly explain how to enable chat/summary.
- **Empty conversation**: sources column + chat + Studio all show sensible empty states; suggested demo questions
  (US-20) still apply in demo.
- **Collapsing panels**: collapsing the conversations (or sources) column must not lose the active selection or
  in-flight chat.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The app MUST present a **config-first** step (set an API key, or continue in demo) before the full
  workspace; the workspace renders once access is configured or demo is chosen.
- **FR-002**: The workspace MUST be a **multi-column layout** with a **collapsible conversations list**, a **sources**
  column, the **chat**, and a **Studio** column; all columns MUST react to a single shared **active-conversation**
  state.
- **FR-003**: A document MUST be **pinnable to a conversation**; uploading while a conversation is active MUST pin the
  new document to it.
- **FR-004**: The **sources** column MUST show the active conversation's documents; **retrieval** for a question MUST
  be scoped to the active conversation's sources.
- **FR-005**: Each conversation MUST have its **own folder tree** (folders are conversation-scoped); drag-drop into
  folders and multi-select bulk operations MUST continue to work within the active conversation's sources column.
- **FR-006**: The **Studio** column MUST provide at least one working visualization — an **AI summary** of the active
  conversation's sources — generated on the BYOK or demo key, with clear empty/disabled states; other Studio tiles
  MAY be upcoming placeholders.
- **FR-007**: The change MUST preserve **session isolation** (a conversation and its sources belong to the session),
  BYOK/demo key handling, quota, folders, and the RAG pipeline; existing behaviour stays green.
- **FR-008**: The conversation↔folder/document links MUST be delivered with a **migration** (not applied at app
  startup, §VIII). Legacy (null-conversation) folders/documents are hidden from the per-conversation view; demo
  documents remain global read-only; deleting a conversation cascades its folders, sources, and chunks.

### Key Entities

- **Conversation** (existing, US-18): now the **owner of a set of sources**; drives the active state for all columns.
- **Source (Document)**: gains a **conversation link**; the sources column and retrieval are scoped by it.
- **Active-conversation state**: one shared signal the conversations, sources, chat, and Studio columns all read.
- **Studio summary**: an AI-generated overview of the active conversation's sources (reuses generation + retrieval).
- **Config gate**: the first-run signal (key set OR demo available) that unlocks the workspace.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A first-time visitor reaches the 4-column workspace only after a visible config step; all four columns
  react to one active-conversation selection, 100% of the time.
- **SC-002**: A document uploaded in conversation A appears in A's sources and in **0** other conversations; a
  question in A grounds only on A's sources.
- **SC-003**: Drag-drop into folders and multi-select bulk ops still succeed within the sources column (existing
  tests stay green).
- **SC-004**: The Studio summary produces a generated overview of the active conversation's sources when sources +
  a key/demo exist, and a clear empty state otherwise, in 100% of cases.
- **SC-005**: All four test tiers remain green; the conversation↔source link ships via a migration, and legacy/demo
  document behaviour is deterministic per the Clarifications.

## Assumptions

- The app has no production data (a portfolio/case-study project), so a simple legacy-document strategy is acceptable
  (per Clarifications) rather than a complex backfill.
- Quota stays **session-wide** (not per conversation) — a conversation is a view over the session's allowance.
- The first working Studio visualization is a **summary**; other tiles (mind map, presentation, etc.) are future work.
- Onboarding does not add authentication — the anonymous-session model (US-01) stays; "config" = API key or demo.

## Dependencies

- Cross-cutting over US-01–US-20 (all merged) — this reorganises the finished product and links documents to
  conversations (US-18 + US-04/07/13).

## Out of Scope

- Real authentication / user accounts.
- Additional Studio visualizations beyond the summary (mind map, presentation, audio, reports) — future work.
- Per-conversation quota; multi-tenant sharing of conversations.
