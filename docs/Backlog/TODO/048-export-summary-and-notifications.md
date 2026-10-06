# 048 — Export Conversation Summary and In-App Notifications

**Status:** TODO
**Sequence:** 48
**Dependencies:** 008 (conversation summaries), 022 (chat history sidebar / conversation view)

---

## Summary

Add an **Export summary** button to the conversation view that downloads the conversation's summary as a Markdown file. If the conversation has no summary yet, clicking the button generates one in the background (the same way embeddings are generated) and notifies the user when it is ready, at which point it can be downloaded.

This needs a small **in-app notification system**: a toast when something finishes, plus a bell icon in the layout with an unread-count badge that opens a list of recent notifications. The same mechanism should also announce when a conversation **import** completes.

## Background

- Imported conversations carry an LLM-generated `StoredConversation.Summary`, produced by `SummarisationService`. Today that service only runs as a bulk job (`POST /conversations/summarise`) over every unsummarised conversation. There is no way to summarise a single conversation on demand.
- Long-running work already runs in the background through bounded channels and hosted services: imports (`Channel<ImportJobRequest>` → `ImportProcessingService`) and embedding (`Channel<EmbedJobRequest>` → `EmbedProcessingService`), with progress tracked in `ImportJobStore` and polled by the UI (`/conversations/status/{jobId}`, `/conversations/embed/latest`).
- There is currently no way for the server to tell the user that background work has finished. The user has to poll a status page.

## Requirements

### Export summary

1. Add an **Export summary** button to the imported-conversation view in `Chat.razor` (the view shown for `/chat/conversation/{ConversationId}`).
2. If the conversation has a summary, clicking the button downloads `<sanitised title>.md`. The file contains the title as a heading, the conversation date, and the summary text.
3. If the conversation has no summary:
   - clicking the button queues summary generation for **that conversation only** in the background, and the button shows a pending state while it runs;
   - when generation finishes, a notification is raised (see below) that links back to the conversation or offers the download directly.
4. Generating a single summary must reuse `SummarisationService`'s prompt and storage path (`UpdateSummaryAsync`), not a separate summarisation implementation. Factor out a single-conversation method as needed.
5. Requests for a conversation that is already being summarised are de-duplicated rather than queued twice.

### Notifications

6. A server-side notification store holding, per user (or the single anonymous user when auth is off): id, kind (e.g. `SummaryReady`, `ImportCompleted`, `ImportFailed`), title, message, optional link, created time, and read/unread.
7. A way for the UI to receive notifications promptly. Options to evaluate: SSE stream, SignalR (the web app is Blazor Server, so a server-side event bus pushed through the circuit may suffice), or polling. Record the choice in an ADR if it adds infrastructure.
8. UI:
   - a **toast** when a notification arrives;
   - a **bell icon** in the layout with a **count badge** for unread notifications;
   - clicking the bell lists recent notifications; opening or clearing them marks them read.
9. Raise notifications from:
   - background summary generation (this issue);
   - **import completion** (success with counts, or failure with the error). `ImportProcessingService` already knows when a job ends.
   Embedding-run completion is a natural third source and should be easy to add.
10. Notifications persist across page reloads, so a summary that finishes while the user is away still shows as unread.

## Acceptance Criteria

- [ ] Export summary downloads a Markdown file for a conversation that has a summary.
- [ ] For a conversation without a summary, the button triggers background generation for that conversation only, with a visible pending state.
- [ ] A toast appears and the bell badge increments when the summary is ready; the summary can then be downloaded.
- [ ] Completing (or failing) a conversation import raises a notification.
- [ ] The bell lists recent notifications and marks them read; the badge reflects the unread count.
- [ ] Notifications survive a page reload.
- [ ] Unit tests cover single-conversation summarisation, de-duplication of requests, and notification creation for summary and import events.

## Notes

- Chat sessions: exporting a session's summary depends on issue 049, which adds a whole-session summary generated when a session completes. Until then, this issue covers imported conversations only; extend the button to sessions once 049 lands.
- Keep the notification store behind a repository interface in Contracts, with Mongo and Postgres implementations, like the other stores.
- The Markdown file name must be sanitised for file systems (titles can contain `/`, `:` and similar characters).
