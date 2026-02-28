# ADR-007: Initial Cloud Provider Selection for LLM and Vector Store Integrations

**Date:** 2026-02-28
**Status:** Accepted
**Related Issues:** [038-add-cloud-provider-integrations.md](../Done/038-add-cloud-provider-integrations.md)

### Context

MattGPT uses provider-agnostic abstractions for both LLM access (`IChatClient` / `IEmbeddingGenerator` via Microsoft.Extensions.AI — see ADR-001) and vector storage (`IVectorStore` — see issue 021). The local development story is proven with Ollama, FoundryLocal, and Qdrant. Azure OpenAI is the only current cloud LLM option, and there are no alternative vector store implementations.

To make MattGPT viable for production deployment and to give users flexibility in provider choice, we need to add cloud/hosted provider support. This ADR documents the selection of the initial set of providers — three LLM providers and three vector databases — chosen to balance ecosystem coverage, community adoption, .NET SDK maturity, and diversity of vendor.

### Decision

#### LLM Providers

We will add support for three cloud LLM providers:

| # | Provider | Rationale |
|---|----------|-----------|
| 1 | **Anthropic (Claude)** | Top-tier model quality; strong developer adoption; the `Anthropic` .NET SDK supports `IChatClient` via Microsoft.Extensions.AI adapters. Claude is many developers' preferred alternative to OpenAI. |
| 2 | **OpenAI (direct)** | The most widely-used hosted LLM API. The `OpenAI` NuGet package is already a project dependency (used by FoundryLocal). Adding a direct OpenAI mode is low-effort — same SDK, different endpoint/key, no Azure wrapper. |
| 3 | **Google Gemini** | Provides vendor diversity beyond the Microsoft/OpenAI ecosystem. Strong multimodal capabilities and competitive pricing. The `Microsoft.Extensions.AI.Google` or community adapter can bridge to `IChatClient`. |

This brings the total LLM provider count to six: Ollama, FoundryLocal, AzureOpenAI (existing) + Anthropic, OpenAI, Gemini (new).

#### Vector Store Providers

We will add support for three cloud/managed vector store providers:

| # | Provider | Rationale |
|---|----------|-----------|
| 1 | **Azure AI Search** | First-party Microsoft service with excellent .NET SDK (`Azure.Search.Documents`) and an Aspire integration. Supports hybrid search (vector + full-text + semantic ranking). Natural choice for teams already on Azure. |
| 2 | **Pinecone** | The most popular purpose-built managed vector database. Fully serverless SaaS with no infrastructure to manage. Strong .NET client (`Pinecone.NET`). Good choice for users who want a dedicated vector DB without cloud vendor lock-in. |
| 3 | **Weaviate** | Popular open-source vector database with a managed cloud offering (Weaviate Cloud Services). Supports hybrid search and has a .NET client. Offers self-hosted and cloud flexibility. Already identified as a candidate in issue 021. |

This brings the total vector store provider count to four: Qdrant (existing) + Azure AI Search, Pinecone, Weaviate (new).

### Consequences

**Easier:**
- Users can deploy MattGPT with their preferred cloud LLM provider without code changes — just configuration.
- Teams on Azure get a natural path via Azure AI Search for vectors and Azure OpenAI or Anthropic for LLM.
- The diversity of providers reduces vendor lock-in risk and broadens the project's appeal.
- Adding further providers in the future follows the same pattern (new `case` in the switch, new `IVectorStore` implementation).

**More difficult:**
- Six LLM providers and four vector stores increase the testing surface. Not all combinations can be integration-tested in CI — provider-specific tests will need API keys.
- Anthropic does not offer a native embedding API. Users choosing Anthropic for chat may need a separate provider (e.g. OpenAI or Voyage) for embeddings, adding configuration complexity.
- The Google Gemini Microsoft.Extensions.AI adapter ecosystem is less mature than Anthropic or OpenAI. Adapter availability should be verified at implementation time.
- Each new NuGet dependency adds to the project's package footprint and potential version conflicts.

### Alternatives Considered

| Alternative | Reason not selected (at this stage) |
|-------------|--------------------------------------|
| **AWS Bedrock** | Good multi-model service, but the .NET SDK experience for M.E.AI integration is less mature. Could be added later. |
| **Cohere** | Strong embedding model (Cohere Embed), but less popular as a primary chat provider. Could be added as an embedding-only option later. |
| **Mistral AI** | Good models, but the API is OpenAI-compatible — users can point the OpenAI direct provider at Mistral's endpoint. Dedicated support isn't needed yet. |
| **Milvus / Zilliz** | Popular vector DB, but Zilliz Cloud adoption trails Pinecone. Could be added later. |
| **ChromaDB** | Primarily a local/embedded vector DB; the cloud offering is newer and less proven for production. |
| **pgvector (PostgreSQL)** | Leverages existing PostgreSQL infra, but adds a full RDBMS dependency just for vectors. Better suited as a later addition for teams already running PostgreSQL. |
| **Redis Vector Search** | Interesting for teams already on Redis, but vector search is secondary to Redis's core use case. |
