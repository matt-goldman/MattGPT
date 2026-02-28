# ADR-004: Multi-Turn Chat with Server-Side Sessions and Rolling Summaries

**Date:** 2026-02-27
**Status:** Accepted
**Related Issues:** [018-multi-turn-chat-with-rolling-summaries.md](../Backlog/Done/018-multi-turn-chat-with-rolling-summaries.md), [019-persist-and-embed-chat-conversations.md](../Backlog/Done/019-persist-and-embed-chat-conversations.md)

### Context

The chat UI currently sends each message as a standalone RAG query — `ChatRequest` contains a single `Message` string, `RagService.ChatAsync` accepts only `string query`, and `BuildMessages` produces exactly two chat messages: `[System, User]`. The Blazor `Chat.razor` page tracks messages locally for display but only forwards the latest message to `POST /chat/stream`.

This makes follow-up questions impossible: the LLM has no memory of prior turns. A user asking "Can you elaborate on the second point?" after a detailed RAG-augmented answer gets a blank-slate response.

The fix must work well with **small-context local LLMs** (4K–8K tokens). The design should be persistence-first — sessions are stored in MongoDB from day one so that issue 019 (embed completed sessions into Qdrant for RAG retrieval) can build on stable infrastructure without reworking the session layer.

### Decision

#### 1. Server-side session state with MongoDB persistence (Option B from the issue)

Introduce a `ChatSessionService` that manages chat sessions persisted in MongoDB, keyed by a `Guid sessionId`. The client receives a session ID when the chat page loads and includes it with every message.

**Why server-side sessions over stateless (sending full history from the client):**

- The client stays thin — it sends `{ sessionId, message }` instead of the full message array.
- Rolling summaries are computed server-side and never leave the server; the client doesn't need to understand the summarisation protocol.
- All prompt construction logic remains encapsulated in the API service.

**Why persist to MongoDB from the start** (rather than in-memory first, then add persistence in issue 019):

- An in-memory `ConcurrentDictionary` would be throwaway work — issue 019 replaces it with MongoDB immediately.
- Without persistence, a page refresh nukes the rolling summary the LLM has been building, leaving the user with a degraded experience.
- The `ChatSession` document model serves both session management and future embedding (issue 019). Designing it once avoids a data-model migration.
- The MongoDB integration already exists (`IConversationRepository`); adding a `ChatSessionRepository` follows the same pattern with minimal effort.
- Sessions surviving API restarts is a natural expectation for a tool that builds long-term memory.

#### 2. Three-tier memory model

The prompt is assembled from three tiers of memory:

| Tier | Scope | Content in prompt | Budget |
|------|-------|-------------------|--------|
| **Long-term** | All past conversations (imports + prior chats) | RAG-retrieved excerpts (unchanged from today) | Existing RAG budget (TopK × 4,000 chars) |
| **Medium-term** | Older turns in the current session | Rolling summary (LLM-compressed) | ~200–500 tokens |
| **Short-term** | Recent turns in the current session | Raw user/assistant messages verbatim | Remaining context budget |

The prompt structure becomes:

```
[System] You are Matt's AI assistant...
[System] === YOUR MEMORIES === (RAG excerpts, as today)
[System] === CONVERSATION SO FAR === (rolling summary, when present)
[User/Assistant] (recent messages verbatim — the "short-term" window)
[User] (current query)
```

#### 3. Rolling summary trigger and generation

- **Context budget:** A new `ChatOptions.MaxConversationTokens` setting (default 2,048 tokens, estimated as chars ÷ 4) defines the token budget for conversation history in the prompt. This is separate from the RAG excerpt budget.
- **Recent window:** The last N messages (configurable via `ChatOptions.RecentMessageCount`, default 6) are always included verbatim.
- **Trigger:** Before building the prompt, if the total estimated token count of all session messages exceeds `MaxConversationTokens`, the oldest messages outside the recent window are compressed.
- **Generation:** A single LLM call with a dedicated summarisation prompt: _"Summarise the conversation so far, preserving key facts, decisions, user preferences, and open questions. Be concise."_ If a prior rolling summary exists, it is included as input along with the newly aged-out messages, making the update incremental.
- **Timing:** The summary is generated **synchronously before the chat response** when triggered. This adds latency on the turn that triggers summarisation, but guarantees the context is accurate. An asynchronous post-response approach was considered but rejected for v1 because it introduces race conditions if the user sends another message before the summary completes.

#### 4. Configuration via `ChatOptions`

A new options class, bound from `appsettings.json` section `"Chat"`:

```csharp
public class ChatOptions
{
    public const string SectionName = "Chat";
    public int MaxConversationTokens { get; set; } = 2048;
    public int RecentMessageCount { get; set; } = 6;
    public string SummaryPrompt { get; set; } = "Summarise the conversation so far...";
}
```

This keeps `RagOptions` focused on retrieval and introduces a clean separation for conversation-management settings.

#### 5. API contract changes

- `ChatRequest` gains an optional `SessionId` field: `record ChatRequest(string Message, Guid? SessionId = null)`.
- When `SessionId` is null, the API creates a new session and returns the ID in the response headers (or body).
- The streaming endpoint returns the session ID via an SSE `event: session` frame at the start of the response.
- Backward-compatible: existing clients that send only `{ "message": "..." }` get a new session per request (current single-turn behaviour preserved).

#### 6. Implementation plan (component changes)

| Component | Change |
|-----------|--------|
| **New: `ChatSessionService`** | MongoDB-backed session management. Methods: `GetOrCreateAsync`, `AddMessageAsync`, `GetRecentMessages`, `GetRollingSummary`, `UpdateRollingSummaryAsync`. Wraps a `ChatSessionRepository` for persistence. |
| **New: `ChatSession` model** | `SessionId` (Guid, BsonId), `Title` (auto-generated), `Messages` list (`{Role, Content, Timestamp}`), `RollingSummary` (string, nullable), `CreatedAt`, `UpdatedAt`, `Status` (Active/Completed). Stored in a dedicated MongoDB collection. Designed so issue 019 adds embedding fields without restructuring. |
| **New: `ChatSessionRepository`** | MongoDB CRUD for `ChatSession` documents. Follows the same pattern as `IConversationRepository`. |
| **New: `ChatOptions`** | Configuration POCO as described above. |
| **`RagService`** | `ChatAsync`/`ChatStreamAsync` gain a `ChatSession session` parameter. `BuildMessages` updated to insert rolling summary and recent messages between the system message and user message. |
| **`Program.cs` endpoints** | `/chat` and `/chat/stream` resolve the session via `ChatSessionService`, pass it to `RagService`, and return the session ID. |
| **`Chat.razor`** | Stores the session ID received from the first response. Sends `{ message, sessionId }` on subsequent messages. No other changes needed — the server manages all context. |

### Consequences

**Easier:**

- Follow-up questions work naturally — the LLM sees prior turns via the recent window and rolling summary.
- Small-context LLMs get effective long-conversation memory through summarisation without blowing the context window.
- Clean separation of concerns: `RagService` handles RAG + prompt construction, `ChatSessionService` handles session lifecycle and rolling summary orchestration.
- Sessions survive page refreshes and API restarts — the rolling summary is never lost.
- Issue 019 (embed completed sessions) builds directly on the persisted `ChatSession` model with no rework — just add embedding fields and a Qdrant write step.
- The API remains backward-compatible for single-turn use.

**More difficult:**

- Every message turn writes to MongoDB (two writes: user message append, then assistant message append + optional summary update). For a single-user local tool this is negligible, but adds a hard dependency on MongoDB availability for chat.
- The summarisation trigger adds latency on the turn it fires (~5–15 seconds for a local LLM). Users will see a brief delay.
- Token estimation via `chars ÷ 4` is approximate. For models with very different tokenisation (e.g., CJK-heavy text), accuracy degrades. Acceptable for this stage.

### Alternatives Considered

| Alternative | Reason rejected |
|-------------|----------------|
| **Stateless / client sends full history (Option A)** | Client becomes responsible for maintaining and sending potentially large message arrays. Rolling summary would either need to be exposed to the client or computed redundantly on each request. Doesn't map cleanly to the session model needed for persistence and embedding. |
| **In-memory sessions first, persist later (019)** | Avoids the MongoDB dependency in 018, but the in-memory store is throwaway work that gets replaced immediately in 019. Page refreshes lose the rolling summary — a poor experience for a tool focused on memory. The model would need to be redesigned for persistence anyway. Not worth the intermediate step. |
| **Send full raw history in prompt (no summarisation)** | Would work for cloud LLMs with large context windows, but MattGPT targets local LLMs with 4K–8K context. A 20-turn conversation would easily overflow. |
| **Async background summarisation** | Summary generated after the response, ready for the next turn. Risks stale context if the user sends another message before the summary completes. Adds complexity (background tasks, state synchronisation). Rejected for v1; can be revisited as an optimisation. |
| **Use Semantic Kernel chat history abstractions** | SK provides `ChatHistory` and related plumbing, but we've already chosen `Microsoft.Extensions.AI` as the abstraction layer (ADR-001). Adding SK for session management alone would be over-coupling. The custom `ChatSession` is simpler and purpose-built. |
