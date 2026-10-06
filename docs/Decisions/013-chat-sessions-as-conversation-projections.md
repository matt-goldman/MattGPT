## ADR-013: Chat sessions enter memory as conversation projections

**Date:** 2026-10-06
**Status:** Accepted
**Related Issues:** [049 — Defect: chat sessions are never completed or embedded](../Backlog/Done/049-defect-chat-sessions-never-completed-or-embedded.md)

### Context

Conversations held in MattGPT's own chat UI were persisted as `ChatSession` documents but never became memory. The retrieval stack — `IVectorStore`, `MemoryRetriever`, `search_memories`, `search_memories_keyword`, `ToEmbeddingText`, reranking — is built entirely around `StoredConversation`. For sessions to be retrievable they had to enter that stack somehow.

Retrieval does two things with a hit: the vector store returns its id, title and summary, and then `IConversationRepository.GetByIdsAsync` loads the full document for prompt excerpts and reranking. Keyword search is the conversation repository's own full-text index. Anything that isn't a `StoredConversation` in the conversation repository is therefore invisible to two of the three retrieval paths, whatever the vector store holds.

### Decision

When a session completes, it is **projected** into a `StoredConversation` and upserted into the conversation repository, then embedded through the same `EmbeddingService` code as an import.

- The projection's `ConversationId` is the session id (`Guid` "D" format), so it is a valid point id for every vector store (Qdrant requires a UUID) and there is a one-to-one mapping back to the session.
- A new `StoredConversation.Source` discriminator (`Import` | `ChatSession`) marks projections. Sources pointing at a projection open the session in the UI instead of the imported-conversation view. Conversation browse listings exclude projections, since the sidebar already lists sessions.
- `UserId` is copied from the session, so the existing per-user filtering in the vector store and keyword search scopes projections to their owner with no new code.
- The **session owns its projection**. The session records its own `EmbeddingStatus` (`None` / `Pending` / `Embedded` / `Error`). The bulk embed run skips projections, and the projection is written with status `Summarised` so the bulk summariser never picks it up. Retries go through the session pipeline, so the session status and the projection status cannot drift apart.
- **Lifecycle:** starting a new chat completes the user's other active sessions. A hosted service sweeps for sessions idle past `Chat:IdleTimeout` and processes `Pending` sessions. Completion only flips the status. Summarising, projecting and embedding happen in the background, so starting a chat never waits on an LLM call. Sending a message to a completed session reactivates it and clears its now-stale summary. On its next completion it is re-projected (the upsert overwrites the previous projection) and re-embedded.
- **Whole-session summary:** generated on completion with `SummarisationService`'s single-conversation path, run over the projection. It is stored on both the session (`ChatSession.Summary`, for display and export) and the projection (`Summary`, which feeds the embedding text exactly as it does for imports).
- **Self-exclusion:** the current session's id is excluded from automatic retrieval and from both search tools.

### Consequences

- Retrieval, keyword search, reranking and embedding needed no session-specific code. The extra code is the projection, the lifecycle and the self-exclusion.
- The conversation store now holds two kinds of document. Any new query that lists conversations for browsing must decide whether to include projections. Status counts and diagnostics deliberately include them, because they are memories.
- The projection duplicates the session's messages. Sessions are small next to an imported history, so this is cheap, and the projection can always be rebuilt from the session.
- Optimistic concurrency: the final status write after processing only applies if the session is still `Completed` with the same message count, so a session continued mid-processing is not wrongly marked `Embedded`.
- Existing sessions are backfilled automatically: they are all `Active` with old timestamps, so the first idle sweep completes and embeds them. `POST /conversations/embed` also runs the session backfill, including retrying sessions in `Error`.

### Alternatives Considered

- **Option B — separate embedding path and vector payload for sessions.** This would need session-aware loading in `MemoryRetriever`, a second full-text search over sessions with its own snippet and ranking logic, a second reranking document builder, and a payload discriminator in all five vector stores. It means more code in more places, for no retrieval benefit.
- **Prefixing projection ids (e.g. `chat-<guid>`) instead of a discriminator field.** Rejected because Qdrant point ids must be UUIDs.
- **Embedding synchronously when a new chat starts.** Rejected because it puts summarisation and embedding latency on the first message of the new chat. The idle sweep is needed anyway, because "navigated away" isn't detectable in Blazor Server.
