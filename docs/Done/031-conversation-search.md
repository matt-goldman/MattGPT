# 031 — Conversation History Search

**Status:** Done  
**Sequence:** 31  
**Dependencies:** 007 (store conversations in MongoDB), 010 (store embeddings in Qdrant), 022 (chat history sidebar)

## Summary

Add a dedicated search feature that lets users search their conversation history by typing a query and seeing matching results directly, without relying on the LLM to execute a search tool or surface results via citations. This is a traditional search experience — type a query, see a list of matching conversations/messages.

## Background

Currently, searching conversation history is only possible in two ways:

1. **LLM tool-calling** — The `search_memories` tool performs semantic vector search via Qdrant, but it's invoked at the LLM's discretion during chat. Users have no direct control and there's low confidence the LLM consistently uses the tool.
2. **Sidebar filter** — A client-side text filter on imported conversation titles (`Title.Contains(query)`). This is purely in-memory string matching on already-loaded sidebar items and only applies to the "Imported History" section.

Neither satisfies the need for a user-driven search that returns results across all conversations and message content.

## Requirements

1. **Search UI** — Add a search bar (prominent, easily accessible — e.g. in the sidebar header or as a dedicated section) where users can type a query.

2. **Search results** — Display matching conversations and/or messages in a results list. Each result should show:
   - Conversation title
   - A snippet of the matching content with the query highlighted
   - Date/time of the conversation or message
   - A click action that navigates to the full conversation

3. **Search backend** — Implement a search endpoint that returns relevant results. Two approaches (or a combination) are viable:
   - **Full-text search via MongoDB** — Create a text index on message content and use `$text` queries. Good for exact phrase and keyword matching.
   - **Semantic search via Qdrant** — Reuse the existing embedding infrastructure for meaning-based search. Good for conceptual queries.
   - A combined approach (try text search first, fall back to or supplement with vector search) may provide the best experience.

4. **Scope** — Search should cover both imported ChatGPT conversations and native MattGPT chat sessions.

5. **Performance** — Results should return quickly. Consider pagination or a result limit (e.g. top 20 results).

## Acceptance Criteria

- [x] A search bar is visible and accessible in the UI.
- [x] Typing a query and submitting it returns matching conversations/messages.
- [x] Results show conversation title, matching content snippet, and date.
- [x] Clicking a result navigates to and displays the full conversation.
- [x] Search covers imported conversations (semantic search via Qdrant).
- [x] Results are returned in a reasonable time (< 2 seconds for typical queries).

## Notes

- The implementation approach (full-text vs. semantic vs. hybrid) should be decided during implementation based on what gives the best user experience. The result quality matters more than the technique.
- MongoDB Atlas has built-in full-text search, but for local dev with the Aspire-managed MongoDB container, a standard text index with `$text` operator should work.
- Consider debouncing the search input for a "search as you type" experience.
- This is functionally different from the sidebar filter — the sidebar filter is a quick title-only filter on loaded items; this is a deep search across all content.
