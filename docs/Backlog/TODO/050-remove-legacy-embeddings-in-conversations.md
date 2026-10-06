## Separable: remove legacy embedding storage from the document database

**This should probably be its own issue.** It is a correctness and footprint fix with no dependency on anything above, and bundling it makes 048 a grab bag. Recorded here so it is not lost; split it out and cut this section if you agree.

Embeddings are written to the document database alongside the imported conversation. This predates the vector store: the original implementation stored embeddings in the document DB and computed cosine similarity in application code. When the configurable vector store replaced that, the document-DB write was never removed.

Current state:

- The write itself is not expensive and is not on the read path.
- The embedding field is explicitly excluded from the projection in every implementation **except Postgres**, where three calls select it.
- Those three calls are legacy: the Postgres module has distinct conversation repositories and vector repositories, so nothing in the Postgres path needs embeddings from the conversation document.

Consequences worth confirming rather than assuming:

- Any of those three calls that reaches a prompt or an LLM context is loading embedding vectors into the context window as noise. This is a plausible cause of context bloat and should be verified before being asserted as the cause.
- The stored vectors are indexed in the document DB, so they are also inflating index size and affecting write and query cost there.

Work:

1. Remove the embedding field from the conversation document model and stop writing it on import and re-embed.
2. Remove the three Postgres calls that project it, routing those call sites to the vector repository where they genuinely need vectors.
3. Migration to drop the column/field and its index for both Mongo and Postgres.
4. Confirm no remaining read path depends on it before dropping, including the bulk digest and embed jobs.

## Follow-ups (not in this issue)

- **Rolling / iterative record generation.** An ADR already exists for rolling summaries and the feature is already logged as defective in the backlog; that defect is the natural home for this work rather than a new issue. The approach under consideration is the refine chain — summarise, then update the summary per subsequent exchange. It trades the lost-in-the-middle attention problem for monotonic erosion of early content and non-recoverable error propagation. Needs a single-pass full-context baseline as a live comparator at every context size tested, since refine has negative value where the whole conversation already fits comfortably in context. This applies to `Record` only, never to `Summary`.
- **Structured extraction over prose records.** Extract typed claims per exchange (decision, revision, abandonment, open question, constraint) with turn provenance, resolve supersession across the claim set, then render narrative from resolved structure. Addresses the case these conversations actually exhibit — decisions made and later reversed — which flat prose hides. Candidate schema: claim text, type, source conversation, turn index, timestamp, linked entities, superseded-by. Relational first; a graph index over the same table is a later view, not a rewrite.
- **Record-derived digests as a routing layer.** Once records exist, test digest-from-record for routing plus fine-grained within-conversation retrieval for locating, against the current whole-conversation-chunk approach. A record-derived digest is a denoised large chunk, so the existing large-chunk finding predicts it should win at the routing layer.
- **Retrieval eval set.** 20–30 questions with known answers over known conversations, weighted toward *what was decided*, *what was rejected and why*, *what is still open*. Include at least one conversation with a late reversal as a drift probe, and several unanswerable questions to measure abstention rather than only recall. Hold embedding model and vector store fixed per run — the current corpus cannot support controlled comparison (see Defect, above).
- **Score semantics.** Confirm what the configured vector store actually returns as a match score before using it as a metric. The implementations differ: Weaviate computes similarity locally, while Qdrant, pgVector, Azure AI Search and Pinecone each return their own score. Qdrant returns raw cosine by default, so scores at or near 1.0 warrant investigation rather than being taken at face value.