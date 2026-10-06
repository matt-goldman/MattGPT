# 049 — Defect: Chat Sessions Are Never Completed or Embedded

**Status:** TODO
**Sequence:** 49
**Type:** Defect (against 019)
**Dependencies:** 019 (persist and embed chat conversations), 009 (embeddings), 018 (rolling summaries)

---

## Summary

Issue 019 is marked Done, including the acceptance criteria "Completed chat sessions are embedded in Qdrant and appear in RAG retrieval results" and "A conversation from a previous chat session can influence responses in a new session (via RAG)". Neither is implemented. Chat sessions are persisted and can be reopened, but nothing ever marks a session `Completed`, and nothing embeds sessions. Conversations held through MattGPT's own chat UI therefore never become memory. Only imported ChatGPT conversations are searchable.

## Current Behaviour

- `ChatSession.Status` defaults to `ChatSessionStatus.Active`. `IChatSessionRepository.UpdateStatusAsync` exists with Mongo and Postgres implementations, but **nothing calls it**, so no session ever becomes `Completed`.
- `EmbeddingService` only processes `StoredConversation` documents (imports). It has no knowledge of `ChatSession`, and no other code embeds sessions or upserts them to the vector store.
- `ChatSessionService` maintains a `RollingSummary` once a session exceeds `ChatSessionOptions.MaxConversationTokens`, but it covers only the older messages (outside the recent window), and nothing uses it beyond the prompt.
- Result: a past chat can never be retrieved by automatic RAG or by the `search_memories` / `search_memories_keyword` tools.

## Expected Behaviour (from 019's design)

1. **Lifecycle:** a session becomes `Completed` when the user starts a new chat, or after a configurable inactivity timeout.
2. **Embedding:** completed sessions are embedded through the same pipeline as imports (title + summary + message content), stored with the same vector schema, and retrieved identically to imported conversations.
3. **Resumption:** reopening and continuing a completed session makes it active again, and it is re-embedded when it completes again, so its memory reflects the whole conversation.

## Requirements

1. **Completion triggers**
   - Starting a new chat completes the user's previous active session(s).
   - A background sweep (hosted service) completes sessions idle longer than a configurable `ChatSessionOptions` timeout. This is the safety net, because "navigated away" isn't reliably detectable in Blazor Server.
   - Sending a message to a `Completed` session sets it back to `Active` and marks it for re-embedding.
2. **Embedding**
   - Decide, and record in an ADR, how sessions enter the memory store. Option A: project the session into a `StoredConversation` (with a source discriminator, e.g. `Source = "chat"`), so `IVectorStore.UpsertAsync(StoredConversation, …)`, `MemoryRetriever`, keyword search and `ToEmbeddingText` all work unchanged. Option B: give sessions their own embedding path and vector payload. Option A is the likely fit given the current interfaces.
   - Track embedding state per session (pending / embedded / error) so failures are retried, and so the import embedding run doesn't spin on sessions.
   - Respect `ChatSession.UserId` so sessions only surface in their owner's retrieval.
3. **Session summary**
   - A completed session needs a summary covering the **whole** session, for embedding text and for display, not just the rolling summary of older messages. Generate it on completion, reusing the summarisation prompt where sensible.
   - This is also the summary that issue 048's export would download for chat sessions (048's open question).
4. **Retrieval hygiene**
   - Exclude the current session from its own retrieval results once it has been embedded (resumed sessions would otherwise retrieve themselves).
   - Sources pointing at a chat session must open that session in the UI, not the imported-conversation view.
5. **Backfill:** provide a way (e.g. part of `POST /conversations/embed`, or a one-off sweep) to complete and embed existing sessions that predate the fix.

## Acceptance Criteria

- [ ] Starting a new chat marks the previous active session `Completed`.
- [ ] Sessions idle past the configured timeout are marked `Completed` by a background sweep.
- [ ] Continuing a completed session reactivates it, and it is re-embedded on its next completion.
- [ ] Completed sessions are embedded and returned by automatic RAG and `search_memories`, scoped to their owner.
- [ ] A session does not appear in its own retrieval results.
- [ ] Each completed session has a whole-session summary.
- [ ] Existing sessions can be backfilled.
- [ ] Unit tests cover the lifecycle transitions, embedding of sessions, self-exclusion from retrieval, and user scoping.

## Notes

- This defect was found while adding reranking (issue 047): `ChatSessionStatus.Completed` is never set anywhere in `src/`.
- Issue 019's acceptance criteria were ticked without these parts being implemented. See the correction note in that file.
