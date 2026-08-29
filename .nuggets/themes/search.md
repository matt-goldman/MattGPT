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
