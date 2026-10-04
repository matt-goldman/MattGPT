# Search

## 2026-08-29 — Keyword search semantics differ between the two repository backends

**Context:** Adding `IConversationRepository.SearchTextAsync` (keyword search) alongside the
existing vector search.
**Observation:** The two implementations cannot be made to agree on how bare words combine.
Postgres `websearch_to_tsquery` ANDs unquoted terms; MongoDB `$text` ORs them and relies on
rank ordering to float documents containing more terms. There is no way to get AND semantics
out of `$text` (a query may contain only one `$text` operator). Quoted phrases and `-`
exclusions do behave the same in both, so that is the portable subset the interface promises
and the LLM tool description advertises.
**Pointer:** `src/Common/Contracts/Services/IConversationRepository.cs` (SearchTextAsync remarks),
`src/Infrastructure/MongoDBModule/Services/ConversationRepository.cs`,
`src/Infrastructure/PostgresModule/Services/PostgresConversationRepository.cs`
**Tags:** gotcha, search, cross-backend

## 2026-08-29 — Postgres FTS index only gets used if the expression matches character-for-character

**Context:** Same change; indexing JSONB conversation documents for full-text search.
**Observation:** The GIN index is an *expression* index over
`jsonb_to_tsvector('english'::regconfig, data, '["string"]')`, not a stored column. The
`::regconfig` cast is load-bearing twice: the 2-arg `jsonb_to_tsvector(text, ...)` form is only
STABLE, so it cannot be indexed at all, and any textual difference between the index definition
and the query expression makes the planner ignore the index. Both come from the single
`TextVectorExpression` constant for that reason — inlining it back into either site would be a
silent full-scan regression, not a compile error.
**Pointer:** `src/Infrastructure/PostgresModule/Services/PostgresConversationRepository.cs:20-26`
(constant), `SearchTextAsync`, `EnsureSchemaAsync` (index DDL)
**Tags:** gotcha, search, postgres

## 2026-10-05 — Semantic retrieval goes through MemoryRetriever; remaining source/prompt quirks

**Context:** Splitting `RagService` before adding reranking.
**Observation:** The embed → vector search → MinScore filter → `GetByIdsAsync` pipeline used to be
duplicated in `RagService.AutoRetrieveAsync` and `SearchMemoriesTool`; it is now a single
`MemoryRetriever.RetrieveAsync` used by both, so retrieval changes (e.g. reranking) belong there.
`KeywordSearchMemoriesTool` is the lexical path and does not go through it. Conversation → text
renderings (`ToEmbeddingText`, `ToExcerpt`) live in a `StoredConversation` extension block.
Fixed in the same pass (don't reintroduce): tool sources used to be *overwritten* per invocation
(`LastSources`), losing earlier searches in a turn — they now accumulate in `Sources` and
`RagService` calls `ResetSources()` at the start of each turn; and ToolsOnly used to get
"No relevant memories were found" in its prompt before searching — `BuildMessages` now only says
that when `autoRetrievalRan`.
Still open:
1. `DefaultSystemPrompt` tells the model to use `search_memories` even in `WithPrompt` mode, where
   no tools are offered (the prompt is user-overridable, so it can't simply vary by mode).
2. Keyword-tool scores are rescaled so the best keyword hit is 1.0, so after merging it always
   outranks any cosine hit — the merged source order is not meaningful across the two tools.
**Pointer:** `src/API/MattGPT.ApiService/Services/MemoryRetriever.cs`,
`src/API/MattGPT.ApiService/Extensions/StoredConversationExtensions.cs`,
`src/API/MattGPT.ApiService/Services/RagService.cs` (`ResetToolSources`, `CollectAllSources`, `BuildMessages`),
`KeywordSearchMemoriesTool.cs` (`BuildSources`)
**Tags:** code-path, gotcha, search, rag

## 2026-10-05 — Reranking lives in MemoryRetriever; how a request flows and fails

**Context:** Adding reranking (ADR-012).
**Observation:** Path with `RAG:UseReranking` on: `AiExtensions.AddAiProvider` registers
`AddHttpClient<IReranker, CohereCompatibleReranker>` only when reranking is on →
`MemoryRetriever` takes `IReranker? reranker = null` (MS DI supplies null when unregistered, so a
missing registration silently means vector-only — covered by a container test) →
`RetrieveAsync` asks the vector store for `max(limit, RerankCandidateCount)`, loads *all* candidates,
sends `ToEmbeddingText(RerankDocumentChars)` per candidate → maps reranker indices back onto the
vector hits with `Score` replaced by the relevance score. Any reranker exception (except caller
cancellation) falls back to the vector path with `MinScore`, reusing the already-loaded
conversations. `RerankingEndpoint` is the **full URL including path** on purpose — no path
munging, unlike the OpenAI module's `/v1` append (see pipeline nugget on double `/v1`).
**Gotchas:** scores are on a different scale with reranking on (reranker relevance vs cosine), so
`MinScore`-style tuning and log thresholds don't transfer; keyword-tool results are not reranked.
**Pointer:** `src/API/MattGPT.ApiService/Services/MemoryRetriever.cs` (`RetrieveAsync`),
`Services/CohereCompatibleReranker.cs`, `Extensions/AiExtensions.cs` (registration + `ValidateRerankingOptions`)
**Tags:** code-path, search, rag, reranking
