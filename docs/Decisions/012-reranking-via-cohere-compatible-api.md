## ADR-012: Reranking via a Cohere-compatible rerank API

**Date:** 2026-10-05
**Status:** Accepted
**Related Issues:** [047-experiment-with-graph-rag-and-reranking.md](../Backlog/TODO/047-experiment-with-graph-rag-and-reranking.md)

### Context

Search quality is poor (issue 047). Retrieval takes the nearest conversations by cosine similarity between the query embedding and one embedding per whole conversation, then cuts at a fixed `MinScore`. Bi-encoder similarity over whole conversations is a coarse signal, and a single threshold can't suit every query.

A reranker (cross-encoder) reads the query and each candidate together and scores relevance directly. It is the standard second stage: retrieve broadly, rerank precisely.

Constraints found while evaluating options (October 2026):

- **No .NET abstraction exists.** Microsoft.Extensions.AI has no reranker interface; an `IReranker` is only an open, "not ready for implementation" proposal for a future `Microsoft.Extensions.DataRetrieval` package (dotnet/extensions#7507).
- **None of the supported chat providers reranks.** Ollama still has no rerank API (ollama/ollama#10467); OpenAI, Anthropic and Gemini don't offer one either. So reranking can't simply reuse `LLM:Provider`.
- **The Cohere-style request is a de facto standard.** `POST {model, query, documents, top_n}` → `{results: [{index, relevance_score}]}` is accepted by Cohere, Jina, vLLM, llama.cpp server, Infinity, LiteLLM, and Cohere on Azure AI Foundry. Hugging Face TEI and Voyage use slightly different shapes.

### Decision

1. **Our own minimal `IReranker`** in Contracts (`RerankAsync(query, documents, topN)` → `(index, score)` list), shaped to be swappable for the Microsoft abstraction if it ships.
2. **One implementation, `CohereCompatibleReranker`**: a typed `HttpClient` against `LLM:RerankingEndpoint` (full URL including path, defaulting to Cohere's hosted endpoint), `LLM:RerankingModelId`, and an optional `LLM:RerankingApiKey`. No SDK dependency, so it lives in the API service rather than an infrastructure module.
3. **Reranking is opaque to callers.** It runs inside `MemoryRetriever`, the single retrieval path used by automatic RAG and the `search_memories` tool. Configuration (`RAG:UseReranking`, `RAG:RerankCandidateCount`, `RAG:RerankDocumentChars`) decides how search runs; callers still just ask for a number of results. With reranking on:
   - vector search fetches `RerankCandidateCount` candidates with no score threshold;
   - each candidate's title, summary and messages (capped at `RerankDocumentChars`) are sent to the reranker;
   - the reranker's top results are returned in its order, carrying its relevance score. `MinScore`/`AutoMinScore` are ignored.
4. **Fail open.** If the rerank call fails, retrieval logs a warning and returns the vector-search candidates filtered by `MinScore`, as if reranking were off.
5. **Startup validation.** With reranking on, a model is required, the provider (if set) must be `Cohere`, the endpoint (if set) must be an absolute http(s) URL, and the tuning values must be positive.
6. **The reranking API key never falls back to the chat `ApiKey`**, because the rerank service is usually a different vendor.

### Consequences

- Reranking works with any chat provider, including Ollama, by pointing at a hosted or self-hosted Cohere-compatible service.
- Local-only setups need an extra service (e.g. vLLM, Infinity or llama.cpp serving `bge-reranker-v2-m3`). There is no AppHost container for one yet; that is a possible follow-up.
- Each retrieval with reranking costs one extra HTTP round trip carrying up to `RerankCandidateCount × RerankDocumentChars` characters (default ~120k). Latency and per-request cost go up.
- `ChatSource.Score` is a reranker relevance score when reranking is on and a cosine similarity otherwise. The two scales aren't comparable, so thresholds tuned for one don't transfer.
- Keyword search (`search_memories_keyword`) is not reranked. Its scores are still merged with semantic sources on a different scale.
- Turning reranking on also raises the default embedding length to 32k characters (long-context embedding), so conversations should be re-embedded for consistent vectors.

### Alternatives Considered

- **LLM-as-reranker (ask the chat model to score candidates).** Works with every provider and needs no extra service, but it is slower, costs chat tokens per candidate, and small local models score unreliably. Deferred; it can be added as a second `IReranker` if needed.
- **Also supporting TEI and Voyage formats.** Each needs its own request/response mapping. Deferred until there is a concrete need; TEI users can front it with LiteLLM or use Infinity instead.
- **Waiting for a Microsoft.Extensions abstraction.** The proposal is early and in a separate package with no timeline. The minimal interface keeps a later migration cheap.
- **Exposing reranking parameters to callers** (candidate counts, thresholds per call). Rejected so that search strategy stays a configuration concern, not something `RagService` or the tools reason about.
