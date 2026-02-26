# 015 — Clickable Citation Links in Chat UI

**Status:** TODO
**Sequence:** 15
**Dependencies:** 12 (chat UI with RAG)

## Summary

Make the source citations displayed beneath each assistant message in the chat UI clickable, so users can view the full conversation context that was used by the RAG pipeline. Currently, sources are rendered as static text showing the title, relevance score, and summary. This issue adds the ability to click a citation and view the original conversation.

## Background

When the LLM responds to a user's query, the RAG pipeline retrieves relevant past conversations from Qdrant and passes them as context. The chat UI already displays these sources in a collapsible `<details>` section under each assistant message, showing:

- **Title** (or "Untitled")
- **Relevance score**
- **Summary** (truncated to 2 lines)

The `ChatSource` record (`RagService.cs`) already carries:
```csharp
public record ChatSource(string ConversationId, string? Title, string? Summary, float Score);
```

The `ConversationId` is available but not currently exposed to the user in any actionable way. Conversations are stored in MongoDB as `StoredConversation` documents.

## Requirements

1. **API endpoint** — Add a GET endpoint to the API service (e.g. `GET /api/conversations/{conversationId}`) that returns the full stored conversation from MongoDB.
2. **Clickable citations** — In the chat UI (`Chat.razor`), make each source citation a clickable link or button.
3. **Conversation viewer** — When a user clicks a citation, display the conversation content. Two acceptable approaches (in order of preference):
   - **Inline/modal viewer**: Show the conversation in a modal or expandable panel within the chat page, rendered as readable formatted text (Markdown or styled HTML).
   - **Markdown download**: Generate a Markdown file from the stored conversation and trigger a browser download so the user can read it locally.
4. **Conversation rendering** — The conversation should be displayed in a human-readable format, showing:
   - Conversation title
   - Participants / roles (user, assistant, system, tool)
   - Message content in order
   - Timestamps (if available)

## Implementation Notes

- The `StoredConversation` model in MongoDB already contains the full conversation data needed for rendering.
- The `ConversationId` is already present in `ChatSource` and flows through to the Blazor frontend — it just isn't rendered or linked.
- Consider a reusable `ConversationViewer` Blazor component that could be used elsewhere in the app later.
- For the modal approach, use the existing LumexUI component library if it provides a modal/dialog component, or a simple full-width expandable section.
- Keep the chat page responsive — loading a full conversation should not block the chat interface. Consider lazy loading the conversation content only when the user clicks.

## Acceptance Criteria

- [ ] A new API endpoint exists that returns a stored conversation by its `ConversationId`.
- [ ] Each source citation in the chat UI is clickable (link, button, or similar affordance).
- [ ] Clicking a citation displays the full conversation content in a readable format (modal, inline panel, or downloaded Markdown file).
- [ ] The conversation viewer shows the title, message roles, and message content in order.
- [ ] The chat interface remains responsive while loading conversation content (no UI blocking).
- [ ] Conversations that are not found (e.g. deleted) show a graceful error message rather than crashing.

## Notes

- This is a UX enhancement — keep the implementation simple and functional. A styled modal with message bubbles or a clean Markdown export are both acceptable.
- If the modal approach is chosen, consider adding a "Download as Markdown" button inside the modal as a secondary action.
- The endpoint could also be useful for a future "browse conversations" feature.
