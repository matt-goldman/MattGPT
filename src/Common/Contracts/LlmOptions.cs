namespace MattGPT.Contracts;

public class LlmOptions
{
    public const string SectionName = "LLM";

    /// <summary>
    /// The LLM provider to use.
    /// Supported values: Ollama, FoundryLocal, AzureOpenAI, OpenAI, Anthropic, Gemini.
    /// </summary>
    public string Provider { get; set; } = "Ollama";

    /// <summary>
    /// The chat model identifier (e.g. "llama3.2" for Ollama, deployment name for Azure OpenAI).
    /// </summary>
    public string ModelId { get; set; } = "llama3.2";

    /// <summary>
    /// The embedding model identifier. Defaults to ModelId if not set.
    /// </summary>
    public string? EmbeddingModelId { get; set; }

    /// <summary>
    /// The base endpoint URL for the LLM provider.
    /// </summary>
    public string Endpoint { get; set; } = "http://localhost:11434";

    /// <summary>
    /// API key for the LLM provider. Required for AzureOpenAI, OpenAI, Anthropic, and Gemini.
    /// Optional for FoundryLocal. Not required for Ollama.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Separate embedding provider to use when the primary LLM provider does not support embeddings
    /// (e.g. Anthropic). Supported values: OpenAI, AzureOpenAI, Ollama.
    /// When set, embedding requests are routed to this provider instead of the primary one.
    /// </summary>
    public string? EmbeddingProvider { get; set; }

    /// <summary>
    /// API key for the embedding provider, if different from the primary LLM ApiKey.
    /// </summary>
    public string? EmbeddingApiKey { get; set; }
    
    /// <summary>
    /// The API format of the reranking service. Supported: <c>Cohere</c> (the Cohere-style
    /// <c>/rerank</c> request, also accepted by Jina, vLLM, llama.cpp server, Infinity, LiteLLM and
    /// Cohere on Azure AI Foundry). Defaults to <c>Cohere</c> when not set. None of the chat providers
    /// offers reranking, so this does not fall back to <see cref="Provider"/>.
    /// </summary>
    public string? RerankingProvider { get; set; }

    /// <summary>
    /// The full URL of the reranking endpoint, including its path (e.g. <c>https://api.cohere.com/v2/rerank</c>,
    /// <c>https://api.jina.ai/v1/rerank</c>, or <c>http://localhost:8000/v1/rerank</c> for a local vLLM).
    /// Defaults to Cohere's hosted endpoint when not set.
    /// </summary>
    public string? RerankingEndpoint { get; set; }

    /// <summary>
    /// The API key sent (as a bearer token) to the reranking endpoint. Not sent when unset, which suits
    /// local servers. Deliberately does not fall back to <see cref="ApiKey"/>: the reranking service is
    /// usually a different vendor, and the chat provider's key must not be sent to it.
    /// </summary>
    public string? RerankingApiKey { get; set; }

    /// <summary>
    /// The reranking model to use for ranking search results. Required when
    /// <see cref="RagOptions.UseReranking"/> is true.
    /// </summary>
    public string? RerankingModelId { get; set; }

    /// <summary>
    /// Endpoint for the embedding provider, if different from the primary LLM Endpoint.
    /// </summary>
    public string? EmbeddingEndpoint { get; set; }

    /// <summary>
    /// Aspire connection string name for the chat model (set automatically by the AppHost).
    /// </summary>
    public string? ChatConnectionName { get; set; }

    /// <summary>
    /// Aspire connection string name for the embedding model (set automatically by the AppHost).
    /// </summary>
    public string? EmbeddingConnectionName { get; set; }
}
