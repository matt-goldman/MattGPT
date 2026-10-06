# 048 — Conversation Record Export, Summary Generation on Import, and In-App Notifications

**Status:** TODO
**Sequence:** 48
**Dependencies:** 008 (conversation summaries), 022 (chat history sidebar / conversation view)

---

## Summary

Split the single `Summary` field into two distinct artifacts with separate purposes: a short **search digest** generated on import and embedded, and a full **conversation record** generated on demand and exported as Markdown.

Add an **Export record** button to the conversation view. If no record exists, clicking the button generates one in the background and notifies the user when it is ready.

Fix the defect that **digests are never generated during import**, which leaves the embedding pipeline's summary-embedding branch permanently dead for freshly imported conversations. Add an opt-in **Generate summaries** checkbox to the import UI, and make digest generation correctly trigger re-embedding when it happens after a conversation has already been embedded.

Fix several truncation faults in `BuildPrompt` that can silently produce no digest at all, or produce one that omits the conversation's conclusions.

This needs a small **in-app notification system**: a toast when something finishes, plus a bell icon in the layout with an unread-count badge that opens a list of recent notifications. The same mechanism should also announce when a conversation **import** completes.

## Background

### Two artifacts, not one

The existing `SummarisationService` prompt produces a 3–6 sentence digest optimised for semantic search. That is the correct artifact for retrieval, and it is not the right artifact to hand a user as a record of a conversation. These are different jobs and should be different fields:

| | Digest | Record |
|---|---|---|
| Field | `Summary` (existing) | new field (`Record`) |
| Length | 3–6 sentences | proportional to conversation |
| Generated | on import (opt-in) | on demand |
| Embedded | yes | no |
| Purpose | routing vector for search | human-readable account of topics, decisions, reversals, outputs |

Keeping the digest out of the record's way matters for two reasons beyond tidiness:

1. **Retrieval stability.** The embedding pipeline binds to `Summary` only. Generating a record never touches a vector, so record generation cannot shift embeddings underneath a retrieval eval run.
2. **A better path to the digest for long conversations.** Once a record exists, the digest can be generated *from the record* rather than from a truncated raw conversation — a short-input call with no truncation, over text that has already surfaced the decisions. Import still needs the direct-from-raw path, so both remain; but for any conversation with a record, the truncation problems below stop applying.

The digest prompt itself is **not** a candidate for improvement in this issue. It is doing its job. All summarisation-quality work belongs to the record.

### Defect: digests are never generated on import, and late digests are never embedded

Two ordering faults compound:

1. **Import does not call summarisation.** `POST /conversations/summarise` is the only path that produces digests, and it is manual and corpus-wide. So a conversation imported today has no digest.
2. **Embedding reads the digest conditionally, once.** The embed logic checks whether a summary exists and, if so, embeds it. Because import leaves `Summary` null, that branch never fires on the import path — it is effectively dead code for any new conversation.

The second-order consequence is the more damaging one: if a digest is generated *after* embedding has run for that conversation (which is exactly what the bulk endpoint does), **the digest is never embedded**. Embedding is not re-triggered, so the digest exists in the document store but is invisible to vector search. Digest generation and digest embedding must be causally linked, not coincidentally ordered.

This also means the corpus currently has **inconsistent embedding coverage** — some conversations have digest embeddings, some do not, depending on the order in which jobs happened to be run. Any retrieval comparison made across the corpus in its present state is confounded by this, independently of chunk size or embedding model.

### Historical context: why import-time summarisation was removed

Summarisation originally did run during import. It was removed because Ollama was running on CPU and the added latency was unacceptable at import scale. That constraint no longer holds — inference now runs on GPU and the performance is not comparable.

The original decision was correct for its constraints and is not being reversed on the merits of the reasoning; it is being revisited because the constraint changed. Making it opt-in via checkbox is the right resolution regardless, because the cost scales with corpus size and with whichever provider and model are configured, and neither is known in advance. **The relevant ADR should be superseded rather than edited**, with the constraint change recorded as the reason.

### Defects in `SummarisationService.BuildPrompt`

1. **Truncation marker has the wrong wording.** The loop walks `LinearisedMessages` forward and stops when the budget is exhausted, so what is dropped is the tail. The marker reads `[... earlier messages truncated ...]`; it should read later or remaining. Its position at the end of the message block is already correct for that wording.
2. **`break` discards everything after the first oversized message.** The loop breaks rather than continues, so a single long message mid-conversation terminates collection even when many short messages after it would have fit. The budget is not exhausted at that point — it just could not fit that one line.
3. **A single long first message yields a null digest, silently.** If the first visible message exceeds the budget, `anyVisible` is never set and the function returns null. Combined with the embedding defect above, a conversation that opens with a long paste gets no digest, no digest embedding, and no report of either.
4. **Head-only truncation drops the wrong end.** The prompt asks for key decisions, conclusions and outputs, which are disproportionately at the end of a conversation — and those are exactly what head-only truncation discards. Head-plus-tail with the middle elided is strictly better for this artifact, and is also where a model underweights content anyway, so an explicit cut there loses least.
5. **Footer reserve is hardcoded with no margin.** `MaxPromptChars - headerLen - 300` reserves 300 characters for a footer that is currently close to 300 including the truncation marker. Any edit to the instruction bullets silently overruns. Compute the reserve from the footer string.

### Existing infrastructure

- Long-running work already runs in the background through bounded channels and hosted services: imports (`Channel<ImportJobRequest>` → `ImportProcessingService`) and embedding (`Channel<EmbedJobRequest>` → `EmbedProcessingService`), with progress tracked in `ImportJobStore` and polled by the UI (`/conversations/status/{jobId}`, `/conversations/embed/latest`).
- There is currently no way for the server to tell the user that background work has finished. The user has to poll a status page.

## Requirements

### Field split

1. Add a `Record` field to `StoredConversation`, distinct from `Summary`. Nullable; absent for all existing conversations.
2. `Summary` keeps its current meaning, prompt and embedding behaviour. No change to the digest prompt beyond the truncation fixes in this issue.
3. `Record` is never embedded. The embedding pipeline must continue to bind to `Summary` only.
4. Record generation for long conversations is out of scope here — v1 generates the record with a single full-conversation call, subject to the same context limits as the digest. See Follow-ups.
5. Where a record exists, digest generation should take the record as input rather than the raw conversation. Where it does not, the existing raw path applies.

### `BuildPrompt` fixes

6. Correct the truncation marker wording to reflect that later messages are dropped.
7. Replace `break` with `continue` so an oversized message does not terminate collection, and emit the marker if any message was skipped.
8. When a message exceeds the remaining budget and no message has been collected yet, truncate that message's content to fit rather than returning null. `BuildPrompt` must not return null for a conversation that has visible content.
9. Change truncation from head-only to head-plus-tail, eliding the middle, with the marker placed at the elision point.
10. Derive the footer reserve from the footer text rather than hardcoding a character count.
11. Where `BuildPrompt` legitimately returns null (no visible messages at all), that outcome must be recorded and reportable, not silent.

### Export record

12. Add an **Export record** button to the imported-conversation view in `Chat.razor` (the view shown for `/chat/conversation/{ConversationId}`).
13. If the conversation has a record, clicking the button downloads `<sanitised title>.md`, containing the title as a heading, the conversation date, and the record.
14. If the conversation has no record:
    - clicking the button queues record generation for **that conversation only** in the background, and the button shows a pending state while it runs;
    - when generation finishes, a notification is raised (see below) that links back to the conversation or offers the download directly.
15. Requests for a conversation already being summarised or recorded are de-duplicated rather than queued twice.
16. Factor out single-conversation methods (`SummariseConversationAsync(conversationId)` and the record equivalent) and have the bulk endpoint, the import path and the export button all call them. No second summarisation implementation.

### Digest generation on import

17. Add a **Generate summaries** checkbox to the import UI, **unchecked by default**. When checked, each conversation in the import has its digest generated as part of the import pipeline.
18. The checkbox must carry a visible warning that it increases import time, with the cost dependent on the configured provider and model. Surface an estimate if the conversation count is known before the job starts.
19. Digest generation on import runs through the same background channel/hosted-service pattern as embedding, not inline in the request, and reports progress through `ImportJobStore` alongside existing import and embed progress.
20. Failure to generate a single conversation's digest must not fail the import. Log it, leave `Summary` null, continue — it can be backfilled later.
21. The import UI does not offer record generation. Records are on-demand only.

### Digest and embedding ordering

22. Digest generation must be ordered **before** embedding for a given conversation when both are requested in the same job, so the existing digest-embedding branch fires naturally.
23. When a digest is generated for a conversation that has **already** been embedded, generating the digest must enqueue a re-embed (or digest-only embed) for that conversation. This closes the dead-digest-embedding defect for the bulk endpoint and the on-demand path.
24. Prefer a digest-only embed over a full re-embed where the store allows targeted upsert, to avoid re-embedding every message chunk for one vector.
25. Provide a way to identify conversations whose digest exists but has no corresponding embedding, so the current corpus can be reconciled. A diagnostic count on the existing embed-status endpoint is sufficient; a repair endpoint is optional.

### Notifications

26. A server-side notification store holding, per user (or the single anonymous user when auth is off): id, kind (e.g. `SummaryReady`, `RecordReady`, `ImportCompleted`, `ImportFailed`), title, message, optional link, created time, read/unread.
27. A way for the UI to receive notifications promptly. Options to evaluate: SSE stream, SignalR (the web app is Blazor Server, so a server-side event bus pushed through the circuit may suffice), or polling. Record the choice in an ADR if it adds infrastructure.
28. UI:
    - a **toast** when a notification arrives;
    - a **bell icon** in the layout with a **count badge** for unread notifications;
    - clicking the bell lists recent notifications; opening or clearing them marks them read.
29. Raise notifications from:
    - background record generation and background digest generation (this issue);
    - **import completion** (success with counts, or failure with the error). `ImportProcessingService` already knows when a job ends;
    - where an import ran with **Generate summaries** enabled, the completion notification reports how many digests succeeded and how many were skipped or failed.
    Embedding-run completion is a natural third source and should be easy to add.
30. Notifications persist across page reloads, so work that finishes while the user is away still shows as unread.

## Acceptance Criteria

- [ ] `Summary` and `Record` are distinct fields; nothing embeds `Record`.
- [ ] Export record downloads a Markdown file for a conversation that has a record.
- [ ] For a conversation without a record, the button triggers background generation for that conversation only, with a visible pending state.
- [ ] A toast appears and the bell badge increments when the record is ready; it can then be downloaded.
- [ ] `BuildPrompt` returns a non-null prompt for every conversation with at least one visible message, including one whose first visible message alone exceeds the budget.
- [ ] A conversation with one oversized message mid-stream still has its later messages collected.
- [ ] A truncated prompt retains both the opening and the closing of the conversation, with the marker at the elision point and wording that reflects the middle being dropped.
- [ ] Import with **Generate summaries** unchecked behaves exactly as today (no summarisation, no added latency).
- [ ] Import with **Generate summaries** checked produces a digest for each conversation, and each digest has a corresponding embedding when the import completes.
- [ ] Generating a digest for an already-embedded conversation results in that digest being embedded, without a manual re-embed step.
- [ ] A conversation digested via the bulk `POST /conversations/summarise` endpoint is searchable by its digest afterwards.
- [ ] The number of digests lacking embeddings is reportable, and is zero after a reconciliation run.
- [ ] Completing (or failing) a conversation import raises a notification, including digest counts where digests were requested.
- [ ] The bell lists recent notifications and marks them read; the badge reflects the unread count.
- [ ] Notifications survive a page reload.
- [ ] Unit tests cover: each `BuildPrompt` fault above as a regression case; single-conversation digest and record generation; de-duplication of requests; digest-triggered re-embedding; the unchecked-checkbox no-op path; notification creation for digest, record and import events.
- [ ] The ADR that removed import-time summarisation is superseded, recording the CPU-to-GPU constraint change as the reason.

## Notes

- Chat sessions: exporting a session's record depends on issue 049, which adds a whole-session summary generated when a session completes. Until then, this issue covers imported conversations only; extend the button to sessions once 049 lands.
- Keep the notification store behind a repository interface in Contracts, with Mongo and Postgres implementations, like the other stores.
- The Markdown file name must be sanitised for file systems (titles can contain `/`, `:` and similar characters).
- The notification infrastructure built here is the dependency for background summarisation jobs generally, which is the point of building it now rather than later.

