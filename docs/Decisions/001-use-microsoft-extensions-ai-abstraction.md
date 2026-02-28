# ADR-001: Use Microsoft.Extensions.AI as LLM Abstraction Layer

**Date:** 2026-02-25
**Status:** Accepted
**Related Issues:** [003-configure-llm-endpoint.md](../Backlog/Done/003-configure-llm-endpoint.md)

### Context

The MattGPT application needs to interact with LLMs for chat completion and embedding generation. It must support multiple LLM providers — Foundry Local, Ollama, and Azure OpenAI — and switching between them should require only a configuration change, not code changes.

We needed an abstraction that:
- Decouples application code from provider-specific SDKs
- Supports all three target providers
- Is actively maintained and aligned with the .NET ecosystem

### Decision

Use **Microsoft.Extensions.AI** (`Microsoft.Extensions.AI`) as the LLM abstraction layer.

The two core interfaces are:
- `IChatClient` — for chat completion requests
- `IEmbeddingGenerator<string, Embedding<float>>` — for generating vector embeddings

Provider-specific packages wire up the concrete implementations:
- `Microsoft.Extensions.AI.Ollama` → `OllamaChatClient` / `OllamaEmbeddingGenerator`
- `Microsoft.Extensions.AI.OpenAI` → wraps `OpenAI.Chat.ChatClient` and `OpenAI.Embeddings.EmbeddingClient` via `.AsIChatClient()` / `.AsIEmbeddingGenerator()` extension methods — used for both Foundry Local (OpenAI-compatible endpoint) and Azure OpenAI (`Azure.AI.OpenAI`)

The active provider is selected at startup by reading `LLM:Provider` from configuration and registering the appropriate concrete implementation against `IChatClient` and `IEmbeddingGenerator<string, Embedding<float>>`.

### Consequences

**Easier:**
- Application code references only `IChatClient` and `IEmbeddingGenerator` — no provider SDK types leak into business logic
- Adding a new provider (e.g. local Llama.cpp server) requires only a new `case` in the startup switch and, at most, a new thin adapter class
- Switching providers in deployment is a single config change (`LLM:Provider`)

**More difficult:**
- Provider-specific features (e.g. Azure OpenAI content filtering metadata) are not accessible through the abstraction without casting or using a separate typed service
- The `Microsoft.Extensions.AI.Ollama` package is currently in preview; a stable release may introduce breaking changes

### Alternatives Considered

| Alternative | Reason rejected |
|-------------|----------------|
| Semantic Kernel (`Microsoft.SemanticKernel`) | Heavier framework with more coupling; `Microsoft.Extensions.AI` is the lower-level abstraction SK itself builds on |
| Direct provider SDKs with a hand-rolled interface | More maintenance burden; Microsoft.Extensions.AI is now the recommended standard |
| Semantic Kernel connectors only | Would tie us to SK's programming model before it's needed |
