# 032 — Tool-Calling Status Indicators in Chat UI

**Status:** TODO  
**Sequence:** 32  
**Dependencies:** 020 (tool-calling RAG retrieval), 012 (chat UI)

## Summary

Show real-time status indicators in the chat UI when the LLM is invoking tools (e.g. "Searching memories...") instead of just showing a generic spinner. This gives users visibility into what the system is doing and confidence that the search tool is being used.

## Background

Currently, when a user sends a message in the chat UI:

1. A `LumexSpinner` is shown while `_isThinking` is true and no streaming tokens have arrived yet.
2. Once tokens start streaming, the markdown content is rendered incrementally.
3. The SSE protocol emits four event types: `session`, `token`, `sources`, `done`.

When the LLM invokes the `search_memories` tool, the tool call happens server-side inside `chatClient.GetStreamingResponseAsync`. The token stream pauses while the tool executes, but the user only sees the spinner — there is no indication that a tool search is underway. This is especially problematic because:

- There is low confidence that the LLM consistently uses the search tool.
- Showing "Searching memories..." would give users assurance that the tool was invoked.
- As more tools are added in the future, status indicators become essential for transparency.

## Requirements

1. **Server-side SSE events** — Extend the SSE protocol in `ChatEndpoints.cs` to emit tool-related events:
   - `event: tool_start` with data containing the tool name (e.g. `{"tool": "search_memories"}`) when a tool call begins.
   - `event: tool_end` with data containing the tool name when a tool call completes.
   - This may require intercepting tool invocations in the chat pipeline (e.g. via a custom `AIFunction` wrapper or middleware in the `Microsoft.Extensions.AI` pipeline).

2. **Client-side status display** — Update `Chat.razor` to handle the new SSE events:
   - When a `tool_start` event is received, replace the generic spinner with a descriptive status message (e.g. "🔍 Searching memories..." for `search_memories`).
   - When a `tool_end` event is received, revert to the generic "thinking" state (or start showing streaming tokens if they arrive).
   - Support multiple sequential tool calls (show the current tool's status).

3. **Tool display names** — Map tool names to user-friendly display strings:
   - `search_memories` → "Searching memories..."
   - Future tools should be easy to add to this mapping.

4. **Visual design** — The status indicator should:
   - Be visually distinct from the spinner but in the same location.
   - Include a subtle animation (e.g. animated dots or a pulse) to show activity.
   - Not be jarring or distracting.

## Acceptance Criteria

- [ ] When the LLM invokes a tool, the chat UI shows a descriptive status (e.g. "Searching memories...") instead of just a spinner.
- [ ] The status updates in real time via SSE events.
- [ ] When the tool completes and tokens start streaming, the status transitions smoothly to showing the response.
- [ ] The implementation supports multiple tools and is easy to extend with new tool names.
- [ ] The SSE protocol includes `tool_start` and `tool_end` events.

## Notes

- The `Microsoft.Extensions.AI` streaming abstraction may emit `FunctionCallContent` items in the streaming response that could be intercepted. Research how to detect tool invocation start/end in the streaming pipeline.
- An alternative approach is to use `DelegatingChatClient` or middleware to intercept tool calls and write SSE events.
- This is a significant quality-of-life improvement that also helps with debugging and assurance.
