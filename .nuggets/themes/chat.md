# Chat (streaming, sessions)

## 2026-10-06 — Every exit from the stream producer must complete the channel; "empty reply + sources" means a clean, text-less stream

**Context:** Diagnosing an empty assistant reply with sources shown, against llama.cpp via the OpenAI module with `ChatOptions.Reasoning` set.
**Observation:**
1. `RagService.ChatStreamAsync` reads a `Channel` that `ProduceStreamChunksAsync` writes. Any `catch` that logs without `TryComplete` leaves the consumer awaiting forever (the request hangs, no sources). All exits now go through `FailStream` or `Complete` — keep it that way when adding catch blocks.
2. Sources are written only after the LLM stream ends normally, so "empty bubble + sources" can't be an exception path. It means the stream yielded no `TextContent`. Likely cause: a reasoning model whose output is all reasoning — llama.cpp sends it as `reasoning_content`, which the OpenAI SDK may not surface (unverified) as `TextReasoningContent` — or a token-limit stop. The producer now logs update count, finish reason, reasoning chars and content types, and emits an `error` chunk instead of sources.
3. Errors reach the UI as an SSE `event: error` (the 200 has already been sent), from both the producer and the endpoint's own try/catch. The client maps it to `ErrorChatEvent`.
**Also:** chat sessions are never completed or embedded despite issue 019 claiming so — tracked as defect 049.
**Pointer:** `src/API/MattGPT.ApiService/Services/RagService.cs` (`ProduceStreamChunksAsync`, `FailStream`, `StreamStats`), `Endpoints/ChatEndpoints.cs` (`/chat/stream`), `src/UI/MattGPT.ApiClient/Services/ChatService.cs`
**Tags:** gotcha, chat, streaming, llama.cpp, reasoning
