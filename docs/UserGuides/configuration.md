# Configuration

## Overview

MattGPT uses **Azure App Configuration** as the central source of truth for application settings. When running locally with Aspire, the Azure App Configuration emulator runs as a Docker container and is seeded automatically with values from `Orchestration/MattGPT.AppHost/appsettings.json`. Services connect to the store at startup and read their settings from it, falling back to their own `appsettings.json` if the store is unavailable.

In deployed scenarios, the resource maps to a real Azure App Configuration store provisioned by the Aspire Azure provisioner (`azd`). Populate the store via the Azure portal, the `az appconfig` CLI, or your CI/CD pipeline.

### How configuration flows

```
AppHost appsettings.json
    │
    └─► AppHost seeds Azure App Configuration emulator (set-if-not-exists)
                │
                ├─► apiservice reads settings via AddAzureAppConfiguration
                │       └─► falls back to src/API/MattGPT.ApiService/appsettings.json
                │
                └─► webfrontend reads settings via AddAzureAppConfiguration
                        └─► falls back to src/UI/MattGPT.Web/appsettings.json
```

> **Set-if-not-exists seeding:** The AppHost only writes a key to the emulator if it does not already exist. This means you can customise values in the Azure App Configuration emulator and they will survive AppHost restarts.

For provider-specific setup (examples, prerequisites, and notes), see [Integrations](integrations.md).

## LLM Settings

The `LLM` section controls which language model provider is used for chat and embeddings.

```json
{
  "LLM": {
    "Provider": "Ollama",
    "ModelId": "llama3.2",
    "EmbeddingModelId": "nomic-embed-text",
    "Endpoint": "http://localhost:11434"
  }
}
```

### Primary Settings

| Setting | Description | Default |
|---------|-------------|---------|
| `Provider` | LLM backend. Supported: `Ollama`, `FoundryLocal`, `AzureOpenAI`, `OpenAI`, `Anthropic`, `Gemini` | `Ollama` |
| `ModelId` | Chat model name (e.g. `llama3.2` for Ollama, `gpt-4o` for OpenAI, deployment name for Azure) | `llama3.2` |
| `EmbeddingModelId` | Embedding model name. Falls back to `ModelId` if omitted | — |
| `Endpoint` | Base URL of the LLM API | `http://localhost:11434` |
| `ApiKey` | API key. Required for `AzureOpenAI`, `OpenAI`, `Anthropic`, `Gemini`. Optional for `FoundryLocal` | — |

### Embedding Provider Fallback

Some providers (Anthropic, Gemini) don't have native embedding APIs or have limited support. You can configure a separate embedding provider:

| Setting | Description |
|---------|-------------|
| `EmbeddingProvider` | Separate provider for embeddings. Supported: `OpenAI`, `AzureOpenAI`, `Ollama` |
| `EmbeddingApiKey` | API key for the embedding provider (falls back to `ApiKey` if omitted) |
| `EmbeddingEndpoint` | Endpoint for the embedding provider (falls back to `Endpoint` if omitted) |

**Example: Anthropic chat + OpenAI embeddings**
```json
{
  "LLM": {
    "Provider": "Anthropic",
    "ModelId": "claude-sonnet-4-20250514",
    "ApiKey": "sk-ant-YOUR_KEY",
    "EmbeddingProvider": "OpenAI",
    "EmbeddingApiKey": "sk-YOUR_OPENAI_KEY",
    "EmbeddingModelId": "text-embedding-3-small"
  }
}
```

### Reranking

When `RAG:UseReranking` is `true`, retrieval sends a larger set of vector-search candidates to a reranking (cross-encoder) model, which picks and orders the final results. None of the chat providers offers reranking, so it is configured separately. Any service that accepts the Cohere-style rerank request works: Cohere, Jina, vLLM, llama.cpp server, Infinity, LiteLLM, and Cohere on Azure AI Foundry. Ollama has no rerank API.

| Setting | Description | Default |
|---------|-------------|---------|
| `RerankingModelId` | Reranking model name (e.g. `rerank-v3.5` for Cohere, `BAAI/bge-reranker-v2-m3` for a self-hosted model). **Required** when reranking is on | — |
| `RerankingEndpoint` | Full URL of the rerank endpoint, **including the path** (e.g. `https://api.jina.ai/v1/rerank`, `http://localhost:8000/v1/rerank`) | `https://api.cohere.com/v2/rerank` |
| `RerankingApiKey` | Sent as a bearer token. Omit for local servers. Does **not** fall back to `ApiKey`, so your chat provider's key is never sent to the rerank service | — |
| `RerankingProvider` | API format. Only `Cohere` (Cohere-compatible) is supported | `Cohere` |

**Example: OpenAI chat + Cohere reranking**
```json
{
  "LLM": {
    "Provider": "OpenAI",
    "ModelId": "gpt-4o",
    "ApiKey": "sk-YOUR_OPENAI_KEY",
    "EmbeddingModelId": "text-embedding-3-small",
    "RerankingModelId": "rerank-v3.5",
    "RerankingApiKey": "YOUR_COHERE_KEY"
  },
  "RAG": {
    "UseReranking": true
  }
}
```

If the rerank service fails at query time, retrieval logs a warning and falls back to plain vector search, so chat keeps working.

## Document DB Settings

The `DocumentDb` section controls where conversations and chat sessions are stored.

```json
{
  "DocumentDb": {
    "Provider": "MongoDB"
  }
}
```

| Setting | Description | Default |
|---------|-------------|---------|
| `Provider` | Document database backend. Supported: `MongoDB`, `Postgres` | `MongoDB` |

> **Note:** MongoDB is managed automatically by Aspire when running locally. When using `Postgres`, the same Postgres instance can serve as both the document DB and the vector store — see [Integrations](integrations.md#postgres).

## Vector Store Settings

The `VectorStore` section controls where embeddings are stored and searched.

```json
{
  "VectorStore": {
    "Provider": "Qdrant"
  }
}
```

| Setting | Description | Default |
|---------|-------------|---------|
| `Provider` | Vector store backend. Supported: `Qdrant`, `Postgres`, `AzureAISearch`, `Pinecone`, `Weaviate` | `Qdrant` |
| `Endpoint` | Endpoint URL (required for `AzureAISearch`, `Weaviate`) | — |
| `ApiKey` | API key (required for `AzureAISearch`, `Pinecone`; optional for `Weaviate`) | — |
| `IndexName` | Index or collection name | `conversations` |

> **Note:** Qdrant and Postgres are managed automatically by Aspire when running locally. Cloud vector stores require manual setup — see [Integrations](integrations.md).

## RAG Settings

The `RAG` section controls retrieval behaviour.

```json
{
  "RAG": {
    "Mode": "Auto",
    "TopK": 5,
    "MinScore": 0.5,
    "AutoTopK": 2,
    "AutoMinScore": 0.65,
    "ToolMaxResults": 5,
    "ChunkingStrategy": "WholeConversation",
    "RetainedConversations": 4,
    "RetainedRelativeScore": 0.6,
    "NeighbourWindow": 1
  }
}
```

### Modes

| Mode | Behaviour |
|------|-----------|
| `WithPrompt` | Full automatic RAG injection on every message. No tools registered. Best for models that don't support tool calling (e.g. `llama3.2` 3B). |
| `Auto` | Light auto-RAG (uses `AutoTopK`/`AutoMinScore`) on the **first message of a chat only**, plus a `search_memories` tool the LLM can call for deeper retrieval. Follow-up messages rely on the conversation so far and the tool. Best for tool-capable models (e.g. `llama3.1` 8B+, GPT-4o). |
| `ToolsOnly` | No automatic RAG injection. The LLM must explicitly call the `search_memories` tool. Best for high-capability models where you want minimal context waste. |

### Settings

| Setting | Description | Default |
|---------|-------------|---------|
| `Mode` | RAG mode (see above) | `WithPrompt` |
| `TopK` | Conversations retrieved per query in `WithPrompt` mode | `5` |
| `MinScore` | Minimum cosine similarity (0.0–1.0) in `WithPrompt` mode | `0.5` |
| `AutoTopK` | Conversations retrieved in `Auto` mode's light pass | `2` |
| `AutoMinScore` | Minimum similarity for `Auto` mode's light pass | `0.65` |
| `ToolMaxResults` | Maximum results per `search_memories` tool invocation | `5` |
| `DiagnosticMode` | When `true`, the LLM outputs structured JSON with reasoning (logged at Information level). Adds latency due to server-side buffering. | `false` |
| `UseReranking` | Rerank vector-search candidates with a reranking model (see [Reranking](#reranking)). When on, `MinScore`/`AutoMinScore` are ignored and long-context embedding is enabled. Requires `LLM:RerankingModelId` | `false` |
| `RerankCandidateCount` | Vector-search candidates sent to the reranker; it returns `TopK`/`AutoTopK`/the tool's limit from these | `30` |
| `RerankDocumentChars` | Maximum characters of each candidate conversation sent to the reranker. Reranker context windows are small, and all candidates go in one request | `4000` |
| `MaxEmbeddingChars` | Maximum characters of each conversation sent to the embedding model (`WholeConversation` chunking only) | `32000` with reranking, `8000` without |
| `ChunkingStrategy` | How conversations are split into the chunks that are embedded and matched: `WholeConversation`, `Message` or `Exchange` (see [Chunking](#chunking)) | `WholeConversation` |
| `MaxChunkChars` | Maximum characters in one chunk under `Message`/`Exchange`. A longer unit is split, and the parts record the unit they came from | `2000` |
| `ChunkCandidateMultiplier` | Chunk hits requested from the vector store per conversation wanted. Above 1 so one many-chunk conversation cannot fill the candidate set | `4` |
| `RetainedConversations` | Conversations kept for generation after ranking. A caller asking for fewer still gets up to this many | `4` |
| `RetainedRelativeScore` | Fraction of the best score a conversation must reach to be retained (0 to retain by count alone) | `0.6` |
| `NeighbourWindow` | Neighbouring chunks either side of a match included in the generation context (0 disables) | `1` |

### Chunking

A conversation is embedded as one or more **chunks**, and each chunk gets its own vector. Chunk scores pool to their conversation by **maximum**, the reranker then orders conversations (never chunks), and the conversation is what is returned and cited. See [ADR-016](../Decisions/016-the-conversation-is-the-unit-of-retrieval.md).

| Strategy | Chunk unit | Notes |
|----------|-----------|-------|
| `WholeConversation` | The whole conversation | One vector per conversation. The chunk and the conversation are the same object, so pooling and neighbour expansion do nothing and the reranker has only the conversation-level signal. |
| `Message` | One visible message | The most discriminative, and the least self-describing: a user turn alone is often uninterpretable. |
| `Exchange` | A user turn plus the turns that follow it, up to the next user turn | Self-describing, because the question travels with its answer. No claim that an exchange contains a complete decision. |

Which unit retrieves best is an open question, measured in issue 051 phase 3 — the default stays `WholeConversation` until it is answered, because switching it silently would invalidate an existing corpus.

**Changing the strategy requires a full re-embed.** Chunks built under different strategies are not comparable, and nothing about a vector says which strategy produced it, so every conversation records its own:

- `GET /conversations/diagnostics` reports the configured strategy, the strategies the corpus is actually embedded under, and warns when there is more than one or when it does not match the configuration. The Settings → Diagnostics tab shows the same, per conversation in the drill-down table.
- `POST /conversations/reembed` (Settings → Diagnostics → "Re-embed everything…") marks every embedded conversation and chat session as needing embedding again and queues an embedding run. Existing vectors stay until each conversation is rewritten, so search keeps working — with mixed strategies — while the run is in flight.

Chunks are stored **without overlap**, which is only safe because of read-time neighbour expansion: `NeighbourWindow` chunks either side of a match are added to the generation context, so a thought spanning several turns survives chunking. Overlapping windows are merged, so a conversation with two nearby matches contributes each message once. Setting `NeighbourWindow` to 0 removes that cover.

### Tuning Tips

- Increase `TopK`/`AutoTopK` for richer context at the cost of more tokens.
- Lower `MinScore`/`AutoMinScore` to include less similar results (may add noise).
- Raise thresholds to require higher relevance.
- For `Auto` mode, the light pass provides baseline context while the tool enables deeper retrieval on demand.
- With reranking on, raise `RerankCandidateCount` to give the reranker more to choose from (slower, larger requests), or lower `RerankDocumentChars` if the rerank service rejects long inputs.
- Changing `UseReranking` changes the default `MaxEmbeddingChars`, so re-run embeddings afterwards for consistent vectors.
- Answers that miss things spread across several conversations are a breadth problem: raise `RetainedConversations`, or lower `RetainedRelativeScore` so near misses are not cut. Answers polluted by irrelevant conversations are the opposite: raise `RetainedRelativeScore`.
- Under `Message`/`Exchange`, raise `ChunkCandidateMultiplier` if a single long conversation is crowding others out of the results.

## Chat Settings

The `Chat` section controls multi-turn chat sessions and when they become memory.

```json
{
  "Chat": {
    "MaxConversationTokens": 2048,
    "RecentMessageCount": 6,
    "IdleTimeout": "00:30:00",
    "SweepInterval": "00:05:00"
  }
}
```

| Setting | Description | Default |
|---------|-------------|---------|
| `MaxConversationTokens` | Estimated token budget for session history in the prompt. Above it, older messages are compressed into a rolling summary | `2048` |
| `RecentMessageCount` | Messages always included verbatim in the prompt | `6` |
| `SummaryPrompt` | Instruction used to generate the rolling summary | (built in) |
| `IdleTimeout` | How long a session can go without a message before it is completed (summarised and embedded as memory). Starting a new chat completes the previous one immediately | `00:30:00` |
| `SweepInterval` | How often the background sweep completes idle sessions and embeds completed ones | `00:05:00` |

A completed session is searchable like an imported conversation, but only by its owner and never from within the same session. Continuing a completed session reactivates it. It is re-summarised and re-embedded when it next completes.

## Authentication Settings

The `Auth` section controls whether authentication is required and which provider handles it.

```json
{
  "Auth": {
    "Enabled": false,
    "Provider": "Keycloak"
  }
}
```

| Setting | Type | Default | Notes |
|---------|------|---------|-------|
| `Auth:Enabled` | bool | `false` | When `true`, all pages and API endpoints require an authenticated user. Data is automatically scoped per user. |
| `Auth:Provider` | string | `"Keycloak"` | `"Keycloak"` (recommended) or `"Identity"` (legacy). See below. |
| `Auth:UseDocumentDbForAuth` | bool | `true` | **Identity only.** When `true`, the Identity backing store uses the same database as `DocumentDb:Provider` (Postgres) if supported; otherwise falls back to SQLite. |
| `Auth:AuthDbProvider` | string | `"SQLite"` | **Identity only, when `UseDocumentDbForAuth = false`.** Supported: `"SQLite"`, `"Postgres"`. |
| `Auth:Keycloak:Realm` | string | `"mattgpt"` | **Keycloak only.** The Keycloak realm name. |
| `Auth:Keycloak:ClientId` | string | `"mattgpt-web"` | **Keycloak only.** The OIDC client ID for the web frontend. |
| `Auth:Keycloak:ServerUrl` | string | - | **Keycloak only.** Optional override for the Keycloak server / authority base URL used for OIDC discovery and token validation. |
| `ConnectionStrings:keycloak` | string | - | **Keycloak only.** Optional connection string-style value that can be used by the hosting environment (for example, Aspire) to supply the Keycloak base URL / authority. |
| `Auth:Keycloak:Audience` | string | - | **Keycloak only.** Expected JWT audience for API access tokens issued by Keycloak; must match the API client/resource configuration in the realm. |
### `Auth:Provider = "Keycloak"` (default, recommended)

When running locally with Aspire, a Keycloak container is provisioned automatically and a `mattgpt` realm is imported from `Orchestration/MattGPT.AppHost/keycloak/mattgpt-realm.json`. No manual Keycloak setup is required.

The web frontend uses an OIDC authorization code flow with PKCE. After login, the access token is forwarded to the API service as a `Bearer` header and validated against Keycloak's JWKS endpoint. Login and registration are handled by Keycloak's hosted UI.

For production deployments, configure the Keycloak realm and the public OIDC client (with PKCE and the appropriate redirect URIs) and set `Auth:Keycloak:Realm` and `Auth:Keycloak:ClientId` via environment variables or user secrets.

### `Auth:Provider = "Identity"` (legacy)

Uses ASP.NET Core Identity with a local database. Login and registration are handled by the app's built-in `/login` and `/register` pages. The backing store is controlled by `Auth:UseDocumentDbForAuth` and `Auth:AuthDbProvider`.

> **Note:** The default configuration has `Auth:Enabled = false`. Set it to `true` in `appsettings.json` or via user secrets to enable authentication.

## Switching Providers at Runtime

**Local development (emulator):** Update the value in the Azure App Configuration emulator directly (via the Aspire dashboard's resource details, the `az appconfig` CLI pointed at `http://localhost:<PORT>`, or by editing `AppHost/appsettings.json` and removing the emulator's data volume so the next start re-seeds it). Then restart the AppHost and any affected services.

**Deployed:** Update the value in the Azure App Configuration store via the Azure portal or `az appconfig kv set`, then restart the AppHost and any affected services (or use App Configuration's push-refresh feature, **note** this is not currently implemented).

> **Note:** For settings that affect the Aspire resource graph (for example `LLM:Provider`, `VectorStore:Provider`, and `Auth:Enabled` / Keycloak provisioning), you must restart the AppHost so the resource graph is re-evaluated, in addition to restarting any services that consume the setting.

> **Important:** If you change the embedding model, existing embeddings become incompatible. Re-embed by calling `POST /conversations/embed` on the API, or re-import your conversations.

---

← [Previous: Usage](usage.md) | [User Guides](index.md) | [Next: Integrations →](integrations.md)
