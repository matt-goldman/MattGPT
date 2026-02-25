# 009 — Generate Embeddings from Summaries

**Status:** Done
**Sequence:** 9
**Dependencies:** 008 (summaries exist in MongoDB)

## Summary

Generate vector embeddings for each conversation summary using an embeddings model. These embeddings will be stored in Qdrant for similarity search during RAG retrieval.

## Requirements

1. Create an embedding service that:
   - Reads conversations with `ProcessingStatus = Summarised` from MongoDB.
   - Sends each summary to an embeddings model (via the LLM abstraction from issue 003, or a dedicated embeddings endpoint).
   - Stores the resulting embedding vector temporarily (in memory or on the MongoDB document) for handoff to issue 010.
   - Updates `ProcessingStatus` to `Embedded`.
2. Support batching of embedding requests for efficiency.
3. Handle failures gracefully — mark failed conversations and continue.
4. The embedding model should be configurable (same config-driven approach as issue 003).

## Acceptance Criteria

- [x] Summaries are converted to embedding vectors.
- [x] Embedding generation handles errors without aborting the batch.
- [x] `ProcessingStatus` is updated to `Embedded` on success.
- [x] The embedding dimensionality is consistent and matches the model configuration.
- [x] Progress is trackable.

## Notes

- Common embedding models: `text-embedding-ada-002` (Azure OpenAI), `nomic-embed-text` (Ollama), or similar.
- Embedding dimensionality will depend on the model — ensure Qdrant collection is configured to match.
- If issue 010 is implemented simultaneously, embedding + storage can be combined into a single step.
