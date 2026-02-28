# Integrations

MattGPT supports multiple LLM providers and vector store backends. This guide covers setup for each.

## LLM Providers

### Ollama (default)

The default provider. Runs models locally via [Ollama](https://ollama.com/).

**Prerequisites:**
- Install and start Ollama (`ollama serve`)
- Pull required models:
  ```bash
  ollama pull llama3.2
  ollama pull nomic-embed-text
  ```

**Configuration:**
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

**Notes:**
- When running under Aspire, the Ollama container is managed and orchestrated automatically — you don't need a separate Ollama install.
- GPU passthrough is enabled on Windows via `.WithGPUSupport()` in the AppHost.
- CPU-only inference is very slow. GPU acceleration is strongly recommended.

---

### Foundry Local

Microsoft's [Foundry Local](https://learn.microsoft.com/windows/ai/foundry-local/) for on-device inference on Windows.

**Prerequisites:**
- Install Foundry Local
- Start the local server

**Configuration:**
```json
{
  "LLM": {
    "Provider": "FoundryLocal",
    "ModelId": "phi-3.5-mini",
    "EmbeddingModelId": "phi-3.5-mini",
    "Endpoint": "http://localhost:5273/v1"
  }
}
```

**Notes:**
- Uses the OpenAI-compatible API endpoint.
- `ApiKey` is optional (defaults to `"local"` if omitted).

---

### Azure OpenAI

[Azure OpenAI Service](https://learn.microsoft.com/azure/ai-services/openai/) for enterprise-grade cloud inference.

**Prerequisites:**
- Azure subscription with an Azure OpenAI resource
- Deploy chat and embedding models in the Azure portal

**Configuration:**
```json
{
  "LLM": {
    "Provider": "AzureOpenAI",
    "ModelId": "gpt-4o",
    "EmbeddingModelId": "text-embedding-3-small",
    "Endpoint": "https://YOUR_RESOURCE.openai.azure.com/",
    "ApiKey": "YOUR_API_KEY"
  }
}
```

**Notes:**
- `ModelId` and `EmbeddingModelId` must match your Azure deployment names.
- Supports both chat and embeddings natively.

---

### OpenAI (direct)

[OpenAI API](https://platform.openai.com/) accessed directly (not through Azure).

**Prerequisites:**
- OpenAI API key from [platform.openai.com](https://platform.openai.com/)

**Configuration:**
```json
{
  "LLM": {
    "Provider": "OpenAI",
    "ModelId": "gpt-4o",
    "EmbeddingModelId": "text-embedding-3-small",
    "ApiKey": "sk-YOUR_API_KEY"
  }
}
```

**Notes:**
- `Endpoint` is not required — the SDK uses the official OpenAI endpoint by default.
- Supports both chat and embeddings natively.

---

### Anthropic Claude

[Anthropic](https://docs.anthropic.com/) for Claude models.

**Prerequisites:**
- Anthropic API key from [console.anthropic.com](https://console.anthropic.com/)
- A separate embedding provider (Anthropic does not offer an embedding API)

**Configuration:**
```json
{
  "LLM": {
    "Provider": "Anthropic",
    "ModelId": "claude-sonnet-4-20250514",
    "ApiKey": "sk-ant-YOUR_ANTHROPIC_KEY",
    "EmbeddingProvider": "OpenAI",
    "EmbeddingApiKey": "sk-YOUR_OPENAI_KEY",
    "EmbeddingModelId": "text-embedding-3-small"
  }
}
```

**Notes:**
- **Anthropic does not provide an embedding API.** You must configure `EmbeddingProvider` to use a separate service for embeddings (e.g. `OpenAI`, `AzureOpenAI`, or `Ollama`).
- Without an embedding provider, the import pipeline and RAG retrieval will fail.
- Supports tool calling for `Auto` and `ToolsOnly` RAG modes.

---

### Google Gemini

[Google Gemini](https://ai.google.dev/) via the GeminiDotnet SDK.

**Prerequisites:**
- Google AI API key from [aistudio.google.com](https://aistudio.google.com/)

**Configuration:**
```json
{
  "LLM": {
    "Provider": "Gemini",
    "ModelId": "gemini-2.5-flash",
    "ApiKey": "YOUR_API_KEY",
    "EmbeddingProvider": "OpenAI",
    "EmbeddingApiKey": "sk-YOUR_OPENAI_KEY",
    "EmbeddingModelId": "text-embedding-3-small"
  }
}
```

**Notes:**
- Gemini embedding support through the M.E.AI abstraction is limited. Consider using `EmbeddingProvider` for a dedicated embedding service.
- If you don't set `EmbeddingProvider`, the app will attempt to use Gemini for embeddings but this may not work for all models.

---

## Vector Store Providers

### Qdrant (default)

[Qdrant](https://qdrant.tech/) is the default vector store, managed automatically by Aspire.

**Configuration:**
```json
{
  "VectorStore": {
    "Provider": "Qdrant"
  }
}
```

**Notes:**
- No additional configuration needed when running under Aspire — the Qdrant container is started automatically.
- For a remote Qdrant instance, configure connection details through Aspire's connection string mechanism.

---

### Azure AI Search

[Azure AI Search](https://learn.microsoft.com/azure/search/) (formerly Azure Cognitive Search) for cloud-hosted vector search.

**Prerequisites:**
- Azure subscription with an Azure AI Search resource
- The index is created automatically on first upsert

**Configuration:**
```json
{
  "VectorStore": {
    "Provider": "AzureAISearch",
    "Endpoint": "https://YOUR_SERVICE.search.windows.net",
    "ApiKey": "YOUR_ADMIN_KEY",
    "IndexName": "conversations"
  }
}
```

**Notes:**
- Uses the HNSW vector search algorithm.
- The index schema (with vector field, metadata fields, and vector profile) is created automatically if it doesn't exist.
- Uses an admin API key for index creation; a query key can be used if the index is pre-created.

---

### Pinecone

[Pinecone](https://www.pinecone.io/) for managed cloud vector search.

**Prerequisites:**
- Pinecone account and API key
- **The index must be pre-created** in the [Pinecone console](https://app.pinecone.io/) or via the API before use
- Index dimensions must match your embedding model output (e.g. 768 for `nomic-embed-text`, 1536 for `text-embedding-3-small`)

**Configuration:**
```json
{
  "VectorStore": {
    "Provider": "Pinecone",
    "ApiKey": "YOUR_PINECONE_API_KEY",
    "IndexName": "conversations"
  }
}
```

**Notes:**
- `Endpoint` is not required — the Pinecone SDK resolves the index endpoint automatically from the API key and index name.
- Metadata is stored alongside vectors for filtering and retrieval.

---

### Weaviate

[Weaviate](https://weaviate.io/) for self-hosted or cloud vector search.

**Prerequisites:**
- A running Weaviate instance (self-hosted or [Weaviate Cloud](https://console.weaviate.cloud/))
- The class schema is created automatically on first upsert

**Configuration:**
```json
{
  "VectorStore": {
    "Provider": "Weaviate",
    "Endpoint": "http://localhost:8080",
    "ApiKey": "YOUR_API_KEY",
    "IndexName": "conversations"
  }
}
```

**Notes:**
- Uses the REST and GraphQL APIs via `HttpClient` (not the WeaviateNET managed client).
- A `Conversation` class with `vectorizer: none` is created automatically if it doesn't exist.
- `ApiKey` is optional for local instances without authentication.
- For Weaviate Cloud, set `Endpoint` to your cluster URL and provide the API key.

---

## Combining Providers

You can mix and match LLM and vector store providers independently. For example:

| Use case | LLM | Embeddings | Vector Store |
|----------|-----|------------|--------------|
| Fully local | Ollama | Ollama | Qdrant |
| Cloud chat, local vectors | OpenAI | OpenAI | Qdrant |
| Anthropic + Azure stack | Anthropic | AzureOpenAI | AzureAISearch |
| Google + Pinecone | Gemini | OpenAI | Pinecone |

The key constraint: your embedding model must be consistent across import and query. Changing the embedding model invalidates existing embeddings — re-embed via `POST /conversations/embed` after switching.
