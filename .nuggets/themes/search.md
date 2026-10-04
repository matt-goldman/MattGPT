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

## 2026-10-05 — Semantic retrieval goes through MemoryRetriever; tool sources and ToolsOnly prompt have hidden hazards

**Context:** Assessing whether to split `RagService` before adding reranking.
**Observation:** The embed → vector search → MinScore filter → `GetByIdsAsync` pipeline used to be
duplicated in `RagService.AutoRetrieveAsync` and `SearchMemoriesTool`; it is now a single
`MemoryRetriever.RetrieveAsync` used by both, so retrieval changes (e.g. reranking) belong there.
`KeywordSearchMemoriesTool` is the lexical path and does not go through it. Two non-obvious hazards found while tracing:
1. `LastSources` on both tools is **overwritten** per invocation, and `CollectAllSources` reads it
   once at the end of the turn — if the LLM calls a tool twice in one turn (the tool description
   tells it to retry once), the first call's sources vanish from the response.
2. In `RagMode.ToolsOnly`, `EffectiveTopK` is 0 so `BuildMessages` always appends "No relevant
   memories were found for this query." to the system prompt *before* the model has searched —
   which can discourage the very tool call ToolsOnly depends on. Conversely, `DefaultSystemPrompt`
   tells the model to use `search_memories` even in `WithPrompt` mode, where no tools are offered.
Also: keyword-tool scores are rescaled so the best keyword hit is 1.0, so after merging it always
outranks any cosine hit — the merged source order is not meaningful across the two tools.
**Pointer:** `src/API/MattGPT.ApiService/Services/MemoryRetriever.cs`,
`src/API/MattGPT.ApiService/Services/RagService.cs` (`CollectAllSources`, `BuildMessages` memories/no-memories block), `SearchMemoriesTool.cs` (`LastSources` assignment),
`KeywordSearchMemoriesTool.cs` (`BuildSources`)
**Tags:** code-path, gotcha, search, rag
