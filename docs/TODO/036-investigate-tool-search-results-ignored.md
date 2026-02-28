# 036 — Investigate and Fix LLM Ignoring Tool Search Results

**Status:** TODO  
**Sequence:** 36  
**Dependencies:** 020 (tool-calling RAG retrieval), 032 (tool-calling status indicators)

## Summary

When using a tool-calling RAG mode (`Auto` or `ToolsOnly`), the LLM successfully invokes the `search_memories` tool (confirmed by logs) but then ignores or denies the results in its response. The model claims "I can't search your conversation history" or "I don't use the `search_memories` function" even though it just called the tool and received results. This needs investigation and a fix.

## Background

### Observed Behaviour

In testing with `gpt-oss:20b` (which supports tool calling), the following was observed:

1. User asks: "Who is Bubbles?"
2. The LLM asks for more context repeatedly instead of searching.
3. After being told "Bubbles" comes from ChatGPT history, the LLM still doesn't search.
4. When explicitly told the system has a `search_memories` tool, the LLM **denies having it**.
5. Meanwhile, the Aspire logs show: `search_memories tool invoked. Query: sentience, MaxResults: 10` — proving the tool **was** called at some point.

The disconnect between "tool was called" (per server logs) and "model denies having the tool" (per response) suggests the model is either:
- Calling the tool but not incorporating the returned results into its answer.
- Experiencing a context/prompt issue where the tool results aren't being fed back correctly.
- The tool was called for a **different** query/turn, and for the turns where it should have searched, it chose not to.

### Relevant Code

- The tool is defined in `SearchMemoriesTool.cs` and returns formatted text with conversation excerpts.
- Tool registration happens in `RagService.BuildToolChatOptions()` which creates a `ChatOptions` with `ToolMode = ChatToolMode.Auto`.
- The streaming pipeline calls `chatClient.GetStreamingResponseAsync(messages, chatOptions)` — the `Microsoft.Extensions.AI` `FunctionInvokingChatClient` middleware handles the tool call loop.
- The `"search_memories tool invoked"` log is ONLY in `SearchMemoriesTool.SearchMemoriesAsync` — it is NOT logged by the initial auto-retrieval in `RagService.AutoRetrieveAsync` (which logs `"RAG auto-retrieval..."` instead).

### Confirmed Configuration (NOT the problem)

RAG mode is set to `Auto` via `appsettings.json` in the AppHost project. This has been **verified** in two ways:

1. The `search_memories` tool invocation appears in the logs — this tool is only registered in `Auto` or `ToolsOnly` modes, not `WithPrompt`.
2. The auto-retrieval log line explicitly confirms the mode: `RAG auto-retrieval (Mode=Auto): 2 results from Qdrant; 0 meet MinScore threshold of 0.65.`

**Do not investigate configuration as a potential cause.** The mode is correctly set and both the auto-retrieval and tool-calling paths are active.

Note: the default in the API service's own `appsettings.json` is `WithPrompt`, but this is overridden by the AppHost configuration.

## Investigation Areas

1. **Tool result feedback loop** — Verify that the `Microsoft.Extensions.AI` `FunctionInvokingChatClient` is correctly feeding tool results back into the conversation. Check if the tool call → result → LLM continuation cycle is working properly with `gpt-oss:20b`.

2. **System prompt clarity** — The system prompt in `RagService.BuildMessages` tells the model "You are a knowledgeable personal assistant with access to the user's past conversation history." Does it clearly instruct the model to use the `search_memories` tool proactively? Consider adding explicit instructions like "When the user asks about past conversations or topics they may have discussed before, use the search_memories tool to find relevant context."

3. **Model compatibility** — Test with different models (e.g. `gpt-4o`, `llama3.1:8b`) to determine if this is model-specific behaviour. Some models are better at tool calling than others.

4. **Tool description quality** — Review the tool's `description` parameter. Is it clear enough for the model to understand when to use it? Consider making it more prescriptive.

5. **Token budget** — Check if tool results are being truncated or if the context window is too small for the tool results to fit alongside the conversation history.

6. **Logging alignment** — The user saw `Query: sentience` in the logs but was asking about "Bubbles". This suggests the tool was called for a different turn — investigate whether the tool is being called for the right queries.

## Acceptance Criteria

- [ ] Root cause identified and documented.
- [ ] When the LLM invokes `search_memories`, the results are demonstrably used in the response.
- [ ] The LLM does not claim it lacks search capability when tools are registered.
- [ ] The system prompt adequately instructs the model to use available tools.
- [ ] Tested with at least two different models to confirm the fix is not model-specific.

## Notes

- This is likely a combination of prompt engineering and plumbing verification.
- Issue 032 (tool-calling status indicators) will help diagnose this in future by making tool invocations visible in the UI.
- Consider logging the tool result content (at Debug level) to verify what the model receives back.
- The `FunctionInvokingChatClient` from `Microsoft.Extensions.AI` should handle the tool loop automatically, but it's worth verifying the middleware is registered in the DI pipeline.
