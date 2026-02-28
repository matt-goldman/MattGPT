# 010 — Store Embeddings in Qdrant with Metadata

**Status:** Done
**Sequence:** 10
**Dependencies:** 002 (Qdrant in AppHost), 009 (embeddings generated)

## Summary

Store the generated embedding vectors in Qdrant alongside metadata that enables efficient filtering and retrieval during RAG queries.

## Requirements

1. Create (or ensure) a Qdrant collection configured for the embedding dimensionality.
2. For each embedded conversation, store a point in Qdrant containing:
   - **Vector**: The embedding of the summary.
   - **Payload / Metadata**:
     - `conversation_id` (pointer back to MongoDB document)
     - `title`
     - `summary` (the summary text, for direct retrieval without a MongoDB round-trip)
     - `create_time`, `update_time`
     - `default_model_slug`
3. Use the conversation's `id` as the Qdrant point ID (or a deterministic mapping) to support upserts.
4. Handle re-imports gracefully — upsert points so re-processing does not create duplicates.
5. Add a simple search endpoint (e.g. `GET /search?q=...&limit=5`) that queries Qdrant and returns matching conversation summaries.

## Acceptance Criteria

- [x] Embedding vectors are stored in Qdrant with the specified metadata.
- [x] Upsert logic prevents duplicate points on re-import.
- [x] The `/search` endpoint returns relevant results for a free-text query.
- [x] Qdrant data is visible via the Qdrant dashboard or API.

## Notes

- Qdrant supports UUID point IDs natively, which aligns well with the ChatGPT conversation IDs.
- Consider adding payload indexes on `create_time` and `default_model_slug` for filtered search.
