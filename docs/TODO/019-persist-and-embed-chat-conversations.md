# 019 — Persist and Embed Chat Conversations

**Status:** TODO
**Sequence:** 19
**Dependencies:** 018 (multi-turn chat), 007 (MongoDB storage), 009 (embeddings), 010 (Qdrant storage)

## Summary

Chat conversations conducted through the MattGPT UI are currently ephemeral — they exist only in the Blazor page's memory and are lost on page refresh or navigation. This issue adds persistence so that chat conversations are saved to MongoDB and embedded in Qdrant, making the system's memory grow continuously from both imports and live interactions.

## Problem

Today, memory only grows through ChatGPT export imports. A user could have a rich conversation through MattGPT's chat UI — discussing decisions, preferences, project context — and all of that knowledge is lost when the page is closed. The next session starts with a blank slate (aside from RAG retrieval of imported conversations).

This creates an asymmetry: the LLM "remembers" imported ChatGPT conversations but forgets everything discussed through MattGPT itself.

## Design

### Storage

1. **Create a `ChatSession` document** in MongoDB when a new chat session begins (page load or explicit "New Chat"):
   - `SessionId` (UUID)
   - `CreatedAt`, `UpdatedAt` timestamps
   - `Title` (auto-generated from the first user message, or LLM-generated)
   - `Messages` (list of `{Role, Content, Timestamp}`) — the full conversation
   - `RollingSummary` (string, nullable) — the current rolling summary from issue 018
   - `Status` (Active, Completed, Archived)

2. **Append messages** to the session document as the conversation progresses. Each user message + assistant response pair is persisted immediately after the LLM responds.

3. **Session lifecycle**:
   - A session is created on first message.
   - A session is marked `Completed` when the user starts a new chat or navigates away (if detectable), or after a configurable inactivity timeout.
   - Completed sessions are candidates for embedding.

### Embedding

4. **Embed completed sessions** using the same `EmbeddingService` pipeline used for imported conversations:
   - Build embedding text from: title + rolling summary (if available) + message content (up to 8,000 chars, same as imports).
   - Store in Qdrant with the same schema, so RAG retrieval treats them identically to imported conversations.
   - Mark the session with an `EmbeddingStatus` field.

5. **Incremental embedding**: For long-running sessions, consider embedding the rolling summary periodically (not just on completion) so that in-progress conversation knowledge is available to other sessions. This is optional for v1.

### UI

6. **Chat history sidebar** (optional, stretch goal): Show a list of past chat sessions with titles. Clicking one loads the conversation. This is a natural UX extension but not required for the core persistence feature.

## Requirements

1. Define a `ChatSession` model and create a MongoDB repository for it.
2. Modify the chat session management (from issue 018) to persist messages to MongoDB after each turn.
3. On session completion, trigger embedding via `EmbeddingService` so the conversation becomes searchable via RAG.
4. Ensure RAG retrieval treats chat-originated conversations identically to imported ones.
5. Auto-generate a session title (from the first message or via LLM) for display and embedding purposes.
6. Handle page refresh / navigation gracefully — the session should be recoverable or properly finalised.

## Acceptance Criteria

- [ ] Chat conversations are persisted to MongoDB with full message history.
- [ ] Completed chat sessions are embedded in Qdrant and appear in RAG retrieval results.
- [ ] A conversation from a previous chat session can influence responses in a new session (via RAG).
- [ ] Page refresh does not lose the current conversation (session is recoverable via session ID).
- [ ] Chat sessions have auto-generated titles.
- [ ] The system handles concurrent sessions (multiple browser tabs) correctly.

## Notes

- The `StoredConversation` model used for imports and the new `ChatSession` model could potentially share a base or be unified. Evaluate whether it's cleaner to store chat sessions as `StoredConversation` documents with a `Source = "chat"` discriminator, or as a separate collection. A shared model simplifies RAG retrieval; a separate model avoids polluting imported data.
- Rolling summaries from issue 018 serve double duty here: they provide intra-session memory during the conversation AND become high-quality embedding text for long-term RAG memory after the session ends.
- Consider a background job that periodically scans for completed-but-not-embedded sessions, as a safety net in case the inline embedding trigger fails.
- This issue, combined with issue 018, completes the "growing memory" vision: MattGPT gets smarter with every conversation, not just from imports.
