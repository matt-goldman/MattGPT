# 011 — Build RAG Retrieval Pipeline

**Status:** TODO
**Sequence:** 11
**Dependencies:** 010 (embeddings in Qdrant)

## Summary

Implement the retrieval-augmented generation pipeline that, given a user query, finds relevant past conversations and injects them as context into an LLM prompt.

## Requirements

1. Create a RAG service that:
   - Takes a user query string.
   - Generates an embedding of the query using the same embeddings model used for summaries.
   - Queries Qdrant for the top-K most similar conversation summaries.
   - Optionally retrieves the full conversation from MongoDB for richer context.
   - Constructs an LLM prompt that includes:
     - A system message explaining the RAG context.
     - The retrieved conversation summaries (and/or excerpts).
     - The user's query.
   - Calls the LLM and returns the response.
2. The number of retrieved results (K) should be configurable.
3. Implement a simple relevance threshold — exclude results below a minimum similarity score.
4. Expose the RAG pipeline via an API endpoint (e.g. `POST /chat` with `{ "message": "..." }`).

## Acceptance Criteria

- [ ] A user query returns an LLM response augmented with relevant conversation context.
- [ ] The response quality visibly improves when relevant conversations exist vs. when they don't.
- [ ] Retrieved context is included in the LLM prompt in a structured, readable format.
- [ ] The number of retrieved results and similarity threshold are configurable.
- [ ] The endpoint returns both the LLM response and the list of retrieved conversation references.

## Notes

- Consider returning the retrieved sources alongside the answer so the UI can display them.
- Prompt engineering will be important here — the system message should clearly instruct the LLM on how to use the retrieved context.
- Start with summary-only retrieval; full conversation retrieval can be a follow-up enhancement.
