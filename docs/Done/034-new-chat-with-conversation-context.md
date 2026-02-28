# 034 — Start New Chat with Conversation as Context

**Status:** Done  
**Sequence:** 34  
**Dependencies:** 012 (chat UI), 022 (chat history sidebar), 025 (citations)

## Summary

When viewing an imported (read-only) conversation, allow the user to start a new chat that has the viewed conversation pre-loaded as context. Currently, the "Start a New Chat" button clears all state and starts a completely fresh session. This feature would let users ask follow-up questions or continue discussions from their imported ChatGPT history.

## Background

Imported ChatGPT conversations are displayed as read-only in the chat UI. A "Start a New Chat" button appears below the messages, but clicking it calls `StartNewChat()` which clears all state and navigates to `/chat` with no context carried over.

Users who browse their imported history and want to ask follow-up questions or explore a topic further must manually reference the conversation — there's no way to seamlessly continue.

### Approach Options

There are a few ways to implement this:

1. **Citation-based context** — Start a new chat session with a system message or citation that references the imported conversation. The RAG pipeline would include the conversation's summary/content as pre-loaded context. This avoids duplicating message data and works naturally with the existing citation model.

2. **Pre-populated messages** — Copy the imported conversation's messages into a new session as "history" messages. This creates a richer context but risks:
   - Duplicate embeddings when the new session's messages are embedded.
   - Bloated session data for long conversations.

3. **Conversation link/stub** — Store a reference (conversation ID) on the new session. When building the LLM context, load the referenced conversation's content as additional context. No duplication, but requires changes to the chat pipeline.

The **citation-based approach** (option 1) is recommended as the simplest and most consistent with the existing architecture.

## Requirements

1. **UI trigger** — When viewing an imported conversation, show a "Continue this conversation" button (or similar) alongside or replacing the existing "Start a New Chat" button.

2. **New session with context** — Clicking the button should:
   - Create a new chat session.
   - Attach the imported conversation as pre-loaded context (e.g. via a citation or system message that includes the conversation summary and key content).
   - Navigate to the new session's chat view so the user can immediately start typing.

3. **Context visibility** — Show the user that the new chat has context from the imported conversation (e.g. a banner like "Continuing from: [conversation title]" or the conversation appearing as a citation).

4. **No embedding duplication** — The approach should not create duplicate embeddings for content that's already embedded from the import.

## Acceptance Criteria

- [x] A "Continue this conversation" button appears when viewing an imported conversation.
- [x] Clicking the button creates a new chat session with the imported conversation's content available as context.
- [x] The user can immediately send messages in the new session.
- [x] The UI indicates that the new chat is contextually linked to the imported conversation (session title prefixed with "Follow-up:").
- [x] No duplicate embeddings are created.

## Notes

- The citation-based approach is simplest: inject the conversation summary into the new session's system context and include a citation link. This reuses the existing RAG and citation infrastructure.
- An alternative UX pattern: instead of a button, allow users to simply start typing in the input area while viewing an imported conversation, which auto-creates a new session with context.
- Consider what happens if the imported conversation is very long — the context window may need to be limited to the summary rather than full message history.
- This feature could later be extended to allow "forking" a conversation at any point, not just from the end.
