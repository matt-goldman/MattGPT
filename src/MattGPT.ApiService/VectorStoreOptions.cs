namespace MattGPT.ApiService;

/// <summary>Configuration options for the vector store provider.</summary>
public class VectorStoreOptions
{
    public const string SectionName = "VectorStore";

    /// <summary>
    /// The vector store provider to use.
    /// Supported values: Qdrant, AzureAISearch, Pinecone, Weaviate.
    /// </summary>
    public string Provider { get; set; } = "Qdrant";

    /// <summary>
    /// Endpoint URL for the vector store (used by AzureAISearch, Weaviate).
    /// For Azure AI Search: https://YOUR_SERVICE.search.windows.net
    /// For Weaviate: https://YOUR_HOST or http://localhost:8080
    /// </summary>
    public string? Endpoint { get; set; }

    /// <summary>
    /// API key for the vector store (used by AzureAISearch, Pinecone, Weaviate).
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Index or collection name. Defaults to "conversations" for all providers.
    /// Currently, only the Pinecone provider honors this as an override; other providers may use
    /// provider-specific, hard-coded collection or index names.
    /// For Pinecone, this is the index name (must be pre-created in the Pinecone console).
    /// </summary>
    public string IndexName { get; set; } = "conversations";
}
