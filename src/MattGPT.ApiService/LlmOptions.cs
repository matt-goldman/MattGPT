namespace MattGPT.ApiService;

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
