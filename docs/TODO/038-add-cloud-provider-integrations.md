# 038 — Add Cloud LLM and Vector Store Provider Integrations

**Status:** TODO
**Sequence:** 38
**Dependencies:** 003 (LLM config), 021 (vector store abstraction)

## Summary

Add support for additional cloud/hosted LLM providers and vector database providers. The local development story (Ollama, FoundryLocal, Qdrant) is proven; this issue extends the application to first-class cloud providers for production and flexible deployment.

## Problem

MattGPT currently supports three LLM providers (Ollama, FoundryLocal, AzureOpenAI) and one vector store (Qdrant). All LLM providers except AzureOpenAI are local-only. Users who want to deploy with Anthropic Claude, direct OpenAI, or Google Gemini cannot do so without code changes. Similarly, the vector store is limited to Qdrant — users who prefer a managed cloud vector database (Azure AI Search, Pinecone, Weaviate) have no option.

The abstractions (`IChatClient`, `IEmbeddingGenerator<string, Embedding<float>>`, `IVectorStore`) and config-driven provider selection (`LLM:Provider`, `VectorStore:Provider`) are already in place. This issue adds the concrete implementations and Aspire orchestration for the new providers.

## Design

See **ADR-007** for the rationale behind provider selection.

### LLM Providers to Add

| Provider | SDK / Package | `LLM:Provider` value |
|----------|--------------|---------------------|
| Anthropic (Claude) | `Anthropic` + `Microsoft.Extensions.AI` adapter | `Anthropic` |
| OpenAI (direct) | `OpenAI` (already referenced) | `OpenAI` |
| Google Gemini | `Google.Cloud.AIPlatform.V1` or `Mscc.GenerativeAI` + M.E.AI adapter | `Gemini` |

### Vector Store Providers to Add

| Provider | SDK / Package | `VectorStore:Provider` value |
|----------|--------------|------------------------------|
| Azure AI Search | `Azure.Search.Documents` + Aspire integration | `AzureAISearch` |
| Pinecone | `Pinecone.NET` | `Pinecone` |
| Weaviate | `WeaviateNET` or `Weaviate.Client` | `Weaviate` |

### Implementation Approach

1. **API Service (`Program.cs`):**
   - Add new `case` branches in the LLM provider switch for `Anthropic`, `OpenAI`, and `Gemini`.
   - Add new `case` branches in the vector store provider switch for `AzureAISearch`, `Pinecone`, and `Weaviate`.
   - Each vector store implementation must implement `IVectorStore` (Upsert, Search, GetPointCount).

2. **AppHost (`AppHost.cs`):**
   - Add conditional Aspire resource wiring for providers that have Aspire integrations (e.g. Azure AI Search).
   - Pass relevant configuration (API keys, endpoints, connection strings) to the API service via environment variables.

3. **LlmOptions / VectorStoreOptions:**
   - Update XML doc comments to list the new supported provider values.
   - No structural changes needed — the existing options classes are already flexible enough.

4. **New Implementation Classes:**
   - `AzureAISearchVectorStore : IVectorStore`
   - `PineconeVectorStore : IVectorStore`
   - `WeaviateVectorStore : IVectorStore`

## Requirements

1. Add Anthropic as an LLM provider, registering `IChatClient` and `IEmbeddingGenerator` through Microsoft.Extensions.AI.
2. Add OpenAI (direct, non-Azure) as an LLM provider.
3. Add Google Gemini as an LLM provider.
4. Add Azure AI Search as a vector store provider implementing `IVectorStore`.
5. Add Pinecone as a vector store provider implementing `IVectorStore`.
6. Add Weaviate as a vector store provider implementing `IVectorStore`.
7. Update AppHost to conditionally wire Aspire resources for providers that support it.
8. Update `LlmOptions` and `VectorStoreOptions` doc comments.
9. All existing tests must continue to pass.

## Acceptance Criteria

- [ ] Setting `LLM:Provider=Anthropic` with a valid API key registers Claude as the chat and embedding provider.
- [ ] Setting `LLM:Provider=OpenAI` with a valid API key registers OpenAI directly (not via Azure).
- [ ] Setting `LLM:Provider=Gemini` with a valid API key registers Google Gemini.
- [ ] Setting `VectorStore:Provider=AzureAISearch` registers Azure AI Search as the vector store.
- [ ] Setting `VectorStore:Provider=Pinecone` registers Pinecone as the vector store.
- [ ] Setting `VectorStore:Provider=Weaviate` registers Weaviate as the vector store.
- [ ] Each new `IVectorStore` implementation passes basic upsert/search/count operations.
- [ ] The AppHost correctly wires resources and passes configuration for each supported provider.
- [ ] All existing unit tests pass without modification.
- [ ] Provider selection remains purely config-driven — no code changes needed to switch providers.

## Notes

- Anthropic does not natively provide an embedding API. The implementation should either use a separate embedding provider or document that a different `EmbeddingModelId` / provider may be needed for embeddings when using Anthropic for chat. This is a known limitation — many users pair Anthropic chat with OpenAI or Voyage embeddings.
- The OpenAI direct provider is very similar to the existing FoundryLocal case but uses the official OpenAI endpoint and requires a real API key.
- Google Gemini M.E.AI adapter availability should be verified at implementation time; the ecosystem is evolving rapidly.
- This issue may be split into sub-issues if it proves too large for a single implementation pass (e.g. one issue per provider pair).
- Consider whether Aspire community components exist for any of these providers — check with `list integrations` before writing custom wiring.
