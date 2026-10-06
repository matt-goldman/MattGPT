# 050 — Remove Legacy Embedding Storage from the Document Database

**Status:** Done
**Sequence:** 50
**Type:** Tech debt
**Dependencies:** 009 (embeddings), 012 (vector store)

---

## Summary

Embeddings are written to the document database alongside the imported conversation. This predates the vector store: the original implementation stored embeddings in the document DB and computed cosine similarity in application code. When the configurable vector store replaced that, the document-DB write was never removed.

Current state:

- The write itself is not expensive and is not on the read path.
- The embedding field is explicitly excluded from the projection in every implementation **except Postgres**, where three calls select it.
- Those three calls are legacy: the Postgres module has distinct conversation repositories and vector repositories, so nothing in the Postgres path needs embeddings from the conversation document.

Consequences worth confirming rather than assuming:

- ~~Any of those three calls that reaches a prompt or an LLM context is loading embedding vectors into the context window as noise. This is a plausible cause of context bloat and should be verified before being asserted as the cause.~~ Does not affect the correctness of removing this. 
- The stored vectors are indexed in the document DB, so they are also inflating index size and affecting write and query cost there.

Work:

1. Remove the embedding field from the conversation document model and stop writing it on import and re-embed.
2. Remove the three Postgres calls that project it, routing those call sites to the vector repository where they genuinely need vectors.
3. Migration to drop the column/field and its index for both Mongo and Postgres.
4. Confirm no remaining read path depends on it before dropping, including the bulk digest and embed jobs.

## Acceptance Criteria

- [x] `StoredConversation.Embedding` is removed; import and re-embed no longer write vectors to the document DB.
- [x] The Postgres repository no longer reads embeddings from the conversation document (the `excludeEmbedding` read flag is gone).
- [x] Existing documents have the field removed on startup, for both MongoDB and Postgres.
- [x] No read path (including the bulk embed job) depends on the field.

## Resolution

- `IConversationRepository.UpdateEmbeddingAsync(id, vector, status)` is replaced by `UpdateProcessingStatusAsync(id, status)`.
- The three Postgres reads that kept the vector (`GetByStatusAsync`, `GetByStatusesAsync`, `GetByIdsAsync`) needed no rerouting: none of their callers used it. The embed job reads conversation content and writes vectors only to `IVectorStore`; retrieval gets vectors from the vector store's search.
- **Migration:** runs once per process on repository initialisation. Postgres: `UPDATE conversations SET data = data - 'embedding' WHERE data ? 'embedding'` (in `EnsureSchemaAsync`). Mongo: `UpdateMany(Exists("Embedding"), Unset("Embedding"))`. Neither backend had an index on the field (the Postgres full-text index covers string values only), so there was no index to drop.
- Mongo's `StoredConversation` class map now ignores extra elements, so documents still holding the old field deserialize safely.
- **Behaviour change:** previously a vector-store upsert failure still marked the conversation `Embedded`, because the vector was "safe" in the document DB. With the document copy gone, that would leave a conversation that search can never find and that is never retried. A failed upsert now marks it `EmbeddingError`, and the next embed run retries it.
