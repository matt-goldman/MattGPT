namespace MattGPT.ApiService;

/// <summary>Configuration options for the vector store provider.</summary>
public class VectorStoreOptions
{
    public const string SectionName = "VectorStore";

    /// <summary>The vector store provider to use. Currently only "Qdrant" is supported.</summary>
    public string Provider { get; set; } = "Qdrant";
}
