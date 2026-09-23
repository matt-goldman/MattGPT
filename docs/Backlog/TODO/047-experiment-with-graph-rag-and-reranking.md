# Experiment with Graph RAG and reranking models

**Status:** TODO

## Summary

Experiment with Graph RAG and reranking models to improve the quality and relevance of search results. Currently, search results are quite poor, and these techniques could help connect ideas and concepts across imported discussions.

## Acceptance Criteria

- [ ] Research and evaluate Graph RAG approaches
- [ ] Implement a Graph RAG prototype
- [ ] Evaluate performance improvements over current RAG approach
- [ ] Research and evaluate reranking models
- [ ] Implement reranking prototype
- [ ] Compare search result quality before and after implementation
- [ ] Document findings and recommendations for future implementation

## Dependencies

- Current RAG implementation (011-rag-retrieval-pipeline.md)
- Embedding generation pipeline (009-generate-embeddings.md)
- Vector database integration (010-store-embeddings-in-qdrant.md)

## Notes

This experiment should focus on improving search result relevance and connections between related concepts across conversations. Graph RAG could help in connecting ideas that are semantically similar but not directly mentioned in the same conversation, while reranking can improve the ordering of results. A couple of other things worth potentially considering:

- Quick test: evaluate optional wait for summaries before embedding (decision in ADR003; not relevant for GPU or cloud embeddings)
- Provide the model with context about the search/index in use and the embeddings model, to help it craft queries

## Related ADRs

- ADR-003: RAG Approach Selection
- ADR-002: Chat UI Evaluation
- ADR-009: Conversation Search Approach