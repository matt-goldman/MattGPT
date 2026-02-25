namespace MattGPT.ApiService;

public class LlmOptions
{
    public const string SectionName = "LLM";

    /// <summary>
    /// The LLM provider to use. Supported values: Ollama, FoundryLocal, AzureOpenAI.
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
    /// API key for Azure OpenAI or Foundry Local. Not required for Ollama.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Aspire connection string name for the chat model (set automatically by the AppHost).
    /// </summary>
    public string? ChatConnectionName { get; set; }

    /// <summary>
    /// Aspire connection string name for the embedding model (set automatically by the AppHost).
    /// </summary>
    public string? EmbeddingConnectionName { get; set; }
}
