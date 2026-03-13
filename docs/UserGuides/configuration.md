# Configuration

All configuration is in `src/MattGPT.ApiService/appsettings.json`. For provider-specific setup (examples, prerequisites, and notes), see [Integrations](integrations.md).

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
    "ToolMaxResults": 5
  }
}
```

### Modes

| Mode | Behaviour |
|------|-----------|
| `WithPrompt` | Full automatic RAG injection on every message. No tools registered. Best for models that don't support tool calling (e.g. `llama3.2` 3B). |
| `Auto` | Light auto-RAG (uses `AutoTopK`/`AutoMinScore`) plus a `search_memories` tool the LLM can call for deeper retrieval. Best for tool-capable models (e.g. `llama3.1` 8B+, GPT-4o). |
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

### Tuning Tips

- Increase `TopK`/`AutoTopK` for richer context at the cost of more tokens.
- Lower `MinScore`/`AutoMinScore` to include less similar results (may add noise).
- Raise thresholds to require higher relevance.
- For `Auto` mode, the light pass provides baseline context while the tool enables deeper retrieval on demand.

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

### `Auth:Provider = "Keycloak"` (default, recommended)

When running locally with Aspire, a Keycloak container is provisioned automatically and a `mattgpt` realm is imported from `Orchestration/MattGPT.AppHost/keycloak/mattgpt-realm.json`. No manual Keycloak setup is required.

The web frontend uses an OIDC authorization code flow with PKCE. After login, the access token is forwarded to the API service as a `Bearer` header and validated against Keycloak's JWKS endpoint. Login and registration are handled by Keycloak's hosted UI.

For production deployments, configure the Keycloak realm and the public OIDC client (with PKCE and the appropriate redirect URIs) and set `Auth:Keycloak:Realm` and `Auth:Keycloak:ClientId` via environment variables or user secrets.

### `Auth:Provider = "Identity"` (legacy)

Uses ASP.NET Core Identity with a local database. Login and registration are handled by the app's built-in `/login` and `/register` pages. The backing store is controlled by `Auth:UseDocumentDbForAuth` and `Auth:AuthDbProvider`.

> **Note:** The default configuration has `Auth:Enabled = false`. Set it to `true` in `appsettings.json` or via user secrets to enable authentication.

## Switching Providers at Runtime

Update `appsettings.json` and restart the API service. No data migration is required — conversations remain in MongoDB and embeddings in the vector store.

> **Important:** If you change the embedding model, existing embeddings become incompatible. Re-embed by calling `POST /conversations/embed` on the API, or re-import your conversations.

---

← [Previous: Usage](usage.md) | [User Guides](index.md) | [Next: Integrations →](integrations.md)
