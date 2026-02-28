# 022 — Chat History Sidebar with Imported Conversations

**Status:** Done
**Sequence:** 22
**Dependencies:** 018 (multi-turn chat), 007 (MongoDB storage), 012 (chat UI)

## Summary

Add a collapsible sidebar to the chat page that shows both native chat sessions (from the MattGPT UI) and imported ChatGPT conversations. Chat sessions can be resumed by clicking; imported conversations are displayed in a read-only view. This elevates MattGPT from a simple chat interface to a browsable conversation archive.

## Problem

Users have no way to see or navigate their conversation history — neither the chats they've had through MattGPT nor the 2,000+ conversations they imported from ChatGPT. Every visit to the chat page starts with a blank slate and no awareness of what came before.

## Design

### Feasibility Assessment

Both data models already contain all the fields needed for a sidebar without structural changes:
- `ChatSession`: `Title`, `CreatedAt`, `UpdatedAt`, `SessionId`, `Messages`
- `StoredConversation`: `Title`, `CreateTime`, `ConversationId`, `LinearisedMessages`

No new collections, schema changes, or data migrations required.

### Backend

1. **`IChatSessionRepository.ListRecentAsync(int limit)`** — returns recent sessions ordered by `UpdatedAt` descending.
2. **`IConversationRepository.GetByIdAsync(string conversationId)`** — returns a single imported conversation with full messages.
3. **API endpoints**:
   - `GET /chat/sessions` — list recent chat sessions (title, id, timestamps, message count).
   - `GET /chat/sessions/{sessionId}` — get full session with all messages.
   - `GET /conversations/{conversationId}` — get single imported conversation with linearised messages.

### Frontend

4. **`ChatLayout.razor`** — a dedicated layout for the chat page that removes the narrow container constraint, allowing full-width sidebar + chat area.
5. **`ChatSidebar.razor`** — collapsible sidebar component showing:
   - "New Chat" button at top.
   - "Your Chats" section listing native chat sessions with relative timestamps.
   - "Imported History" section with paginated imported conversations and a client-side filter.
6. **Updated `Chat.razor`** — two-column layout with sidebar integration:
   - Clicking a chat session loads it and allows continued conversation.
   - Clicking an imported conversation shows it read-only with an "Imported" badge.
   - Sidebar refreshes automatically after each chat message.
   - Sidebar is collapsible via a hamburger toggle.

## Acceptance Criteria

- [x] Sidebar lists recent chat sessions with titles and relative timestamps.
- [x] Sidebar lists imported conversations with titles and dates.
- [x] Clicking a chat session loads its full message history into the main chat area.
- [x] Clicking an imported conversation displays it in a read-only view.
- [x] "New Chat" button clears the view and starts a fresh session.
- [x] Sidebar is collapsible to preserve screen space.
- [x] Imported conversations section supports client-side filtering and pagination.
- [x] All existing tests pass.

## Notes

- The sidebar reuses the existing `/conversations` paginated endpoint for imported history, adding no new query complexity.
- The `GET /conversations/{conversationId}` endpoint normalises `StoredMessage.Parts` into a single content string, making imported messages render identically to native chat messages.
- Read-only mode for imported conversations hides the input area and shows a "Start a New Chat" button instead.
