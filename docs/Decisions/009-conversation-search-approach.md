# ADR-009: Conversation Search Approach — Semantic Vector Search

**Date:** 2026-03-08
**Status:** Accepted
**Related Issues:** [031-conversation-search](../Backlog/Done/031-conversation-search.md)

### Context

Issue 031 required adding a dedicated search feature for users to search their conversation history. The issue explicitly noted three viable approaches:

1. **Full-text search via MongoDB** — Create a text index on message content and use `$text` queries. Good for exact phrase and keyword matching.
2. **Semantic search via Qdrant** — Reuse the existing embedding infrastructure for meaning-based search. Good for conceptual queries.
3. **Hybrid** — Combine both approaches, e.g. try text search first, fall back to or supplement with vector search.

The implementation needed to cover both imported ChatGPT conversations and native MattGPT chat sessions.

### Decision

The search endpoint (`GET /search`) uses **semantic vector search via Qdrant only**. The query is embedded using the configured embedding model and compared against stored conversation embeddings using cosine similarity.

This approach was chosen because:

- **Infrastructure reuse**: The embedding pipeline (issue 009/010) and Qdrant vector store were already operational. No additional indexing infrastructure was needed.
- **Conceptual search**: Users searching their history are more likely to search by concept ("that conversation about deployment strategies") than by exact keywords. Semantic search handles this naturally.
- **Consistency with RAG pipeline**: The same vector search powers the LLM's `search_memories` tool and automatic RAG retrieval. Using one search mechanism means improvements to embedding quality benefit all search surfaces.
- **Simplicity**: A single search backend avoids the complexity of result merging, score normalisation across different ranking systems, and the UX question of how to present results from two different sources.

### Consequences

**Positive:**
- Single search infrastructure to maintain and optimise.
- Search quality improves automatically when the embedding model is upgraded.
- Conceptual and synonym-aware search works out of the box (e.g. searching "deployment" finds conversations about "CI/CD", "release pipeline", etc.).

**Negative:**
- **Exact keyword search is weak.** Vector search is poor at finding conversations by exact identifier, code symbol, or unusual proper noun that the embedding model hasn't seen. A search for `XyzLibrary` may not find the conversation that mentions it if the embedding model doesn't capture that token well.
- **Search quality is directly tied to embedding model quality.** With a weaker embedding model (e.g. `nomic-embed-text`), similarity scores cluster around 0.4–0.5 even for relevant results, making it hard to distinguish good results from noise. This is a known issue — see the embedding model quality discussion below.
- **Short queries perform poorly.** One- or two-word queries produce mediocre embeddings, especially with smaller models. Users accustomed to keyword search may find the experience frustrating.

### Alternatives Considered

**Full-text search via MongoDB `$text` index:**
Rejected as the sole approach because it cannot handle conceptual or synonym-based queries. A search for "deployment" would not find a conversation titled "Setting up CI/CD pipelines." However, this remains a viable future addition for exact keyword and phrase matching — it would complement rather than replace vector search.

**Hybrid (vector + full-text with result merging):**
Rejected for the initial implementation due to complexity. Merging results from two ranking systems (cosine similarity scores vs. MongoDB text relevance scores) requires score normalisation, de-duplication, and a strategy for combining rankings. The UX also needs consideration — should users see a unified list, or choose between "semantic" and "keyword" search? This is worth revisiting if exact keyword search becomes a frequent user need. If added, the recommended approach is:
- Add a `SearchByTextAsync` method to `IConversationRepository` backed by a MongoDB text index.
- Expose it as a separate query parameter or mode on the existing `/search` endpoint (e.g. `GET /search?q=...&mode=keyword`).
- Keep the two result sets separate in the API response rather than attempting to merge scores.

### Notes

The quality of vector search results is heavily dependent on the embedding model. The default Ollama model (`nomic-embed-text`, 768 dimensions) produces noticeably weaker results than cloud models like OpenAI's `text-embedding-3-small` (1536 dimensions). Upgrading the embedding model is the highest-leverage improvement to search quality across all surfaces (user search, LLM tool, and auto-RAG retrieval).
