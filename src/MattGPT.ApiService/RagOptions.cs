namespace MattGPT.ApiService;

public class RagOptions
{
    public const string SectionName = "RAG";

    /// <summary>Number of similar conversations to retrieve from Qdrant (top-K).</summary>
    public int TopK { get; set; } = 5;

    /// <summary>
    /// Minimum cosine-similarity score (0.0–1.0) required for a result to be included in the prompt.
    /// Results below this threshold are discarded before the prompt is built.
    /// </summary>
    public float MinScore { get; set; } = 0.5f;
}
