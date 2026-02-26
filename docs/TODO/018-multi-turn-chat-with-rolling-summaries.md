# 018 — Multi-Turn Chat with Rolling Summaries

**Status:** TODO
**Sequence:** 18
**Dependencies:** 011 (RAG pipeline), 012 (chat UI)

## Summary

The chat UI currently sends each message as a standalone RAG query — the LLM has no awareness of prior turns in the conversation. This issue adds true multi-turn conversation support with a rolling summarisation strategy that keeps the context window manageable for local LLMs with small context limits (4K–8K tokens).

## Problem

Today, `RagService.ChatAsync()` accepts a single `query` string and builds a prompt with:
- A system message containing RAG-retrieved conversation excerpts (~4,000 chars each, up to 5).
- A single user message containing the query.

The Blazor Chat.razor page maintains `_messages` for display but only sends the latest message text to `POST /chat/stream`. The LLM cannot reference anything said earlier in the session.

This makes follow-up questions impossible:
- User: "What did we discuss about the Qdrant integration?"
- LLM: (gives a good RAG-augmented answer)
- User: "Can you give me more detail on the second point?"
- LLM: (has no idea what "the second point" refers to — it's a brand new context)

## Design: Three-Tier Memory

The solution introduces a layered memory model that gives small-context LLMs effective memory far exceeding their context window:

| Tier | Scope | Mechanism | Context cost |
|------|-------|-----------|-------------|
| **Short-term** | Recent messages in current session | Raw messages passed in the prompt | Full token cost |
| **Medium-term** | Older messages in current session | Rolling summary (LLM-compressed) | ~200–500 tokens |
| **Long-term** | All past conversations (imports + chat) | RAG retrieval from Qdrant | Top-K excerpts |

### Rolling Summary Strategy

1. **Context budget**: Define a configurable `MaxContextTokens` budget (default ~3,000 tokens, adjustable per model). This budget covers the conversation history portion of the prompt — the system message, RAG excerpts, and the current user message have their own budget.
2. **Recent window**: Always include the last N messages verbatim (configurable, default 4–6 messages).
3. **Summary trigger**: When the total conversation history exceeds `MaxContextTokens`, the oldest messages outside the recent window are compressed into a rolling summary.
4. **Summary generation**: The LLM generates a concise summary (~200–500 tokens) of the messages being compressed. The prompt should instruct: "Summarise the conversation so far, preserving key facts, decisions, user preferences, and any open questions."
5. **Incremental updates**: As the conversation continues, the rolling summary is updated incrementally — the previous summary + newly aged-out messages are re-summarised together. This avoids re-processing the entire history each time.
6. **Prompt structure** becomes:
   ```
   [System] You are Matt's AI assistant... 
   [System] === YOUR MEMORIES === (RAG excerpts)
   [System] === CONVERSATION SO FAR === (rolling summary)
   [User/Assistant] (recent messages verbatim)
   [User] (current query)
   ```

### API Changes

The `/chat` and `/chat/stream` endpoints must accept conversation context, not just a single message. Options:
- **Option A (stateless)**: The client sends the full `messages` array and the server manages summarisation. Simpler server, but the client must track messages.
- **Option B (server-side sessions)**: The server maintains session state (in-memory or MongoDB). The client sends a `sessionId` + new message. More complex but enables persistence (see issue 019).

Recommend **Option B** as it naturally supports persistence and keeps the client thin. The session could be keyed by a UUID generated when the chat page loads.

## Requirements

1. Modify `RagService` (or create a `ChatSessionService`) to accept and manage multi-turn conversation history.
2. Implement a rolling summary mechanism that compresses older messages when the conversation exceeds the configured context budget.
3. Update the chat API endpoints to accept/return session context.
4. Update the Blazor Chat.razor page to send conversation history (or session ID) with each request.
5. Make the context budget, recent message window size, and summary behaviour configurable via `RagOptions` or a new options class.
6. The rolling summary prompt should be tunable.

## Acceptance Criteria

- [ ] Follow-up questions work correctly — the LLM can reference prior messages in the session.
- [ ] Conversations exceeding the context budget are automatically summarised without user intervention.
- [ ] The rolling summary preserves key facts, decisions, and context from earlier in the conversation.
- [ ] A long conversation (20+ turns) remains coherent and the LLM can reference early topics via the summary.
- [ ] Context budget, recent window size, and summary prompt are configurable.
- [ ] Performance is acceptable — summarisation does not add excessive latency to each turn (consider doing it asynchronously or only when the budget is exceeded).
- [ ] Existing single-turn behaviour still works if no session context is provided.

## Notes

- Token counting can be approximate (chars ÷ 4 is a reasonable heuristic for English text) — exact tokenisation per model is overkill for this stage.
- Consider whether the rolling summary should be generated by the same LLM as the chat response or a lighter/faster model. For single-model setups, it must be the same model.
- The summary generation adds one extra LLM call when triggered. For very slow models (CPU inference), consider an asynchronous approach: generate the summary in the background after the response, so it's ready for the *next* turn.
- This issue focuses on intra-session memory. Cross-session persistence is covered in issue 019.
- The rolling summary approach is complementary to the RAG pipeline — RAG provides inter-conversation memory while rolling summaries provide intra-conversation memory. Together they allow a small-context LLM to behave as though it has a much larger effective memory.
