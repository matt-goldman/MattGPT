# 012 — Create Chat UI Page for LLM Interaction with RAG Memory

**Status:** TODO
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

- [ ] A `/chat` page exists and is accessible from the nav menu.
- [ ] Users can type messages and receive LLM responses augmented with RAG memory.
- [ ] Retrieved source conversations are displayed alongside each response.
- [ ] The chat supports multi-turn conversation within a session.
- [ ] Loading states and errors are handled gracefully in the UI.

## Notes

- Consider streaming the LLM response for better UX (SSE or SignalR), but a simple request/response is acceptable for MVP.
- The UI should make it clear that responses are enhanced with conversation history context.
- This page, combined with the upload page (issue 005), forms the two main UI surfaces described in the README.
