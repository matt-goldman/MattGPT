namespace MattGPT.ApiService;

/// <summary>
/// Controls how RAG retrieval and tool calling interact.
/// </summary>
public enum RagMode
{
    /// <summary>
    /// Full automatic RAG injection on every message. No tools registered.
    /// Best for models that don't support tool calling (e.g. llama3.2 3B).
    /// </summary>
    WithPrompt,

    /// <summary>
    /// Light automatic RAG (fewer results, higher threshold) plus a <c>search_memories</c>
    /// tool the LLM can invoke for deeper/targeted retrieval.
    /// Best for models with reliable tool calling (e.g. llama3.1 8B+, GPT-4o).
    /// </summary>
    Auto,

    /// <summary>
    /// No automatic RAG injection. The <c>search_memories</c> tool is the only way to
    /// retrieve past conversation context. The LLM must explicitly search.
    /// Best for high-capability models (e.g. GPT-4o) where users want minimal context waste.
    /// </summary>
    ToolsOnly,
}

public class RagOptions
{
    public const string SectionName = "RAG";

    /// <summary>
    /// The RAG mode controlling how retrieval interacts with tool calling.
    /// See <see cref="RagMode"/> for details. Default is <see cref="RagMode.WithPrompt"/>.
    /// </summary>
    public RagMode Mode { get; set; } = RagMode.WithPrompt;

    /// <summary>Number of similar conversations to retrieve from Qdrant (top-K) in <see cref="RagMode.WithPrompt"/> mode.</summary>
    public int TopK { get; set; } = 5;

    /// <summary>
    /// Minimum cosine-similarity score (0.0–1.0) required for a result to be included in the prompt.
    /// Results below this threshold are discarded before the prompt is built.
    /// Used in <see cref="RagMode.WithPrompt"/> mode.
    /// </summary>
    public float MinScore { get; set; } = 0.5f;

    /// <summary>Number of similar conversations to retrieve in <see cref="RagMode.Auto"/> mode's automatic light pass.</summary>
    public int AutoTopK { get; set; } = 2;

    /// <summary>Minimum similarity score for <see cref="RagMode.Auto"/> mode's automatic light pass.</summary>
    public float AutoMinScore { get; set; } = 0.65f;

    /// <summary>Maximum results the <c>search_memories</c> tool can return per invocation.</summary>
    public int ToolMaxResults { get; set; } = 5;

    /// <summary>
    /// When true, the LLM is instructed to respond in a structured JSON format that includes
    /// both its reasoning process and its final response. The reasoning is logged at Information
    /// level for diagnostic analysis. The user always sees only the <c>response</c> field.
    /// In streaming mode, tokens are buffered server-side and released as a single chunk after
    /// JSON parsing, so streaming latency is traded for diagnostic visibility.
    /// Default is <c>false</c>.
    /// </summary>
    public bool DiagnosticMode { get; set; } = false;
}
