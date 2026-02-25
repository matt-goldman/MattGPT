# 012 — Create Chat UI Page for LLM Interaction with RAG Memory

**Status:** Done
**Sequence:** 12
**Dependencies:** 011 (RAG pipeline)

## Summary

Add a Blazor page to the web frontend that provides a chat interface for interacting with an LLM using the stored conversation history as RAG memory.

## Requirements

1. Add a new Blazor page at `/chat` in `MattGPT.Web`.
2. The page should include:
   - A chat message input area.
   - A scrollable conversation display showing user messages and LLM responses.
   - Display of the retrieved source conversations (titles, relevance scores) used to augment each response.
   - An indicator when the LLM is processing (loading/thinking state).
3. Each user message should call the RAG pipeline endpoint (`POST /chat`) and display the result.
4. Conversation history within the chat session should be maintained (multi-turn).
5. Add navigation to the chat page from the existing nav menu.

## Acceptance Criteria

- [x] A `/chat` page exists and is accessible from the nav menu.
- [x] Users can type messages and receive LLM responses augmented with RAG memory.
- [x] Retrieved source conversations are displayed alongside each response.
- [x] The chat supports multi-turn conversation within a session.
- [x] Loading states and errors are handled gracefully in the UI.

## Notes

- Consider streaming the LLM response for better UX (SSE or SignalR), but a simple request/response is acceptable for MVP.
- The UI should make it clear that responses are enhanced with conversation history context.
- This page, combined with the upload page (issue 005), forms the two main UI surfaces described in the README.
- **UI approach evaluated** — OpenWebUI was considered as an alternative to a custom Blazor page. It was rejected for v1 because Aspire's OpenWebUI support (`CommunityToolkit.Aspire.Hosting.OpenWebUI`) is currently Ollama-only, and MattGPT supports multiple LLM providers. See [ADR-002](../Decisions/002-chat-ui-approach-blazor-vs-openwebui.md) for the full analysis. Revisit if Aspire extends OpenWebUI support to arbitrary OpenAI-compatible endpoints.

## Post-Completion Changes

**2026-02-26 — ADR-003:** The Ollama HttpClient timeout was increased from the default 100 seconds to 10 minutes via `ConfigureHttpClientDefaults` in the API service. This prevents 500 errors when llama3.2 on CPU takes longer than 100 seconds to process large RAG prompts (especially after model reloads in memory-constrained Docker containers). See [ADR-003](../Decisions/003-rag-pipeline-v2-embed-from-content.md).
