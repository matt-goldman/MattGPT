# Chat (streaming, sessions)

## 2026-10-06 — Every exit from the stream producer must complete the channel; "empty reply + sources" means a clean, text-less stream

**Context:** Diagnosing an empty assistant reply with sources shown, against llama.cpp via the OpenAI module with `ChatOptions.Reasoning` set.
**Observation:**
1. `RagService.ChatStreamAsync` reads a `Channel` that `ProduceStreamChunksAsync` writes. Any `catch` that logs without `TryComplete` leaves the consumer awaiting forever (the request hangs, no sources). All exits now go through `FailStream` or `Complete` — keep it that way when adding catch blocks.
2. Sources are written only after the LLM stream ends normally, so "empty bubble + sources" can't be an exception path. It means the stream yielded no `TextContent`. Likely cause: a reasoning model whose output is all reasoning — llama.cpp sends it as `reasoning_content`, which the OpenAI SDK may not surface (unverified) as `TextReasoningContent` — or a token-limit stop. The producer now logs update count, finish reason, reasoning chars and content types, and emits an `error` chunk instead of sources.
3. Errors reach the UI as an SSE `event: error` (the 200 has already been sent), from both the producer and the endpoint's own try/catch. The client maps it to `ErrorChatEvent`.
**Also:** sessions were never completed or embedded (defect 049). Fixed 2026-10-06; see the session memory nugget below.
**Pointer:** `src/API/MattGPT.ApiService/Services/RagService.cs` (`ProduceStreamChunksAsync`, `FailStream`, `StreamStats`), `Endpoints/ChatEndpoints.cs` (`/chat/stream`), `src/UI/MattGPT.ApiClient/Services/ChatService.cs`
**Tags:** gotcha, chat, streaming, llama.cpp, reasoning

## 2026-10-06 — How a chat session becomes memory (049 / ADR-013), and the traps around it

**Context:** Fixing defect 049.
**Path:**
1. Completion = status flip only. `ChatSessionService.GetOrCreateAsync` → `CompleteOtherActiveSessionsAsync` (new chat) + `ChatSessionMemorySignal.Notify()`. The idle path is `ChatSessionMemoryService.SweepAsync` → `CompleteIdleSessionsAsync`. Neither bumps `UpdatedAt` (otherwise the idle sweep would reorder the sidebar).
2. `ChatSessionLifecycleService` (hosted, `ChatSessionLifecycleService.cs`) runs `SweepAsync(retryErrors:false)` at startup, every `Chat:SweepInterval`, or when signalled. `EmbedProcessingService` runs it with `retryErrors:true` before the conversation embed.
3. `ChatSessionMemoryService.StoreAsync`: summary (`SummarisationService.GenerateSummaryAsync` over the projection) → `StoredConversation.FromChatSession` → `UpsertAsync` → `EmbeddingService.EmbedOneAsync` → `UpdateMemoryStateAsync` (guarded on Completed + message count).
4. Retrieval: `RagService.PrepareTools` / `AutoRetrieveAsync` pass the session id as `excludeConversationId`. `ChatSource.From` takes the kind from the loaded conversation's `Source`.
**Traps:**
- A projection's `ConversationId` must stay a bare GUID. Qdrant point ids must be UUIDs, so prefixing (`chat-…`) breaks Qdrant.
- Projections are written as `Summarised` (even with a null summary) so the bulk summariser (`GetByStatusAsync(Imported)`) never touches them. `EmbeddingService.EmbedAsync` skips them by `Source`.
- Any new conversation *browse* query must exclude `Source == ChatSession` (Postgres: promoted `source` column), or sessions show up twice in the sidebar.
- `ChatSessionStatus` needed `[JsonStringEnumConverter]` for the same jsonb_set number/string reason as the pipeline nugget. Any enum written via `jsonb_set` or `||` must carry it.
**Tags:** code-path, gotcha, chat, embedding, postgres
