using MattGPT.Contracts.Models;

namespace MattGPT.Contracts;

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
    /// Light automatic RAG (fewer results, higher threshold) on the first user message of a chat,
    /// plus a <c>search_memories</c> tool the LLM can invoke for deeper/targeted retrieval. Follow-up
    /// messages skip automatic retrieval: the model answers from the conversation so far and uses the
    /// tool when it needs more. (If no search tool is registered, every message gets automatic RAG.)
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

    /// <summary>Number of similar conversations to retrieve from the vector store (top-K) in <see cref="RagMode.WithPrompt"/> mode.</summary>
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

    /// <summary>
    /// When true, retrieval fetches a larger candidate set from vector search (<see cref="RerankCandidateCount"/>)
    /// and uses a reranking model to choose and order the results; similarity thresholds (<see cref="MinScore"/>,
    /// <see cref="AutoMinScore"/>) are then ignored. Also enables long-context embedding (see
    /// <see cref="MaxEmbeddingChars"/>). Requires <see cref="LlmOptions.RerankingModelId"/>. If the reranking
    /// service fails at query time, retrieval falls back to plain vector search.
    /// </summary>
    public bool UseReranking { get; set; }

    /// <summary>
    /// Number of vector-search candidates passed to the reranker when <see cref="UseReranking"/> is true.
    /// The reranker then returns the requested number of results (e.g. <see cref="TopK"/>) from these.
    /// Default is 30.
    /// </summary>
    public int RerankCandidateCount { get; set; } = 30;

    /// <summary>
    /// Maximum characters of each candidate conversation (title, summary and messages) sent to the
    /// reranker. Reranking models have small context windows (typically 512–4k tokens for query plus
    /// document) and every candidate is sent in one request, so this is kept well below the embedding
    /// length. Default is 4,000 (~1k tokens).
    /// </summary>
    public int RerankDocumentChars { get; set; } = 4_000;

    /// <summary>
    /// Maximum characters of conversation content (title, summary and messages) sent to the embedding
    /// model per conversation. When not set, defaults to 32,000 (sized for a long-context embedding
    /// model, ~8k tokens) if <see cref="UseReranking"/> is true, or 8,000 otherwise. If the model rejects
    /// text longer than 8,000 characters as exceeding its context window, the embedding is retried with
    /// the text trimmed to 8,000 characters before falling back to chunking.
    /// </summary>
    /// <remarks>Only applies to <see cref="Models.ChunkingStrategy.WholeConversation"/>; the other
    /// strategies are bounded by <see cref="MaxChunkChars"/> instead.</remarks>
    public int? MaxEmbeddingChars { get; set; }

    /// <summary>Default for <see cref="MaxEmbeddingChars"/> without reranking (~2k tokens).</summary>
    public const int DefaultMaxEmbeddingChars = 8_000;

    /// <summary>
    /// Default for <see cref="MaxEmbeddingChars"/> with reranking, which is paired with a long-context
    /// embedding model (~8k tokens).
    /// </summary>
    public const int LongContextMaxEmbeddingChars = 32_000;

    /// <summary>
    /// <see cref="MaxEmbeddingChars"/> when set, otherwise the default for the reranking setting.
    /// </summary>
    public int EffectiveMaxEmbeddingChars => MaxEmbeddingChars
        ?? (UseReranking ? LongContextMaxEmbeddingChars : DefaultMaxEmbeddingChars);

    // ── Chunking (ADR-016) ──

    /// <summary>
    /// How conversations are divided into the chunks that are embedded and matched against. Selectable
    /// per deployment so chunking can be compared the same way providers and stores are.
    /// </summary>
    /// <remarks>
    /// Changing this invalidates every stored vector: a conversation's chunks are only comparable with
    /// chunks built the same way. Each conversation records the strategy its vectors were built under
    /// (<see cref="Models.StoredConversation.EmbeddedChunkingStrategy"/>), the diagnostics endpoint
    /// reports a corpus embedded under more than one, and <c>POST /conversations/reembed</c> rebuilds
    /// the corpus under the configured strategy. The default stays
    /// <see cref="Models.ChunkingStrategy.WholeConversation"/> — the chunk unit is a measured choice,
    /// not an assumed one, and switching the default silently would invalidate an existing corpus.
    /// </remarks>
    public ChunkingStrategy ChunkingStrategy { get; set; } = ChunkingStrategy.WholeConversation;

    /// <summary>
    /// Maximum characters in one chunk under the <see cref="Models.ChunkingStrategy.Message"/> and
    /// <see cref="Models.ChunkingStrategy.Exchange"/> strategies. A unit longer than this is split, and
    /// the parts carry the ordinal of the unit they came from. Default is 2,000 (~500 tokens), which
    /// fits every embedding model MattGPT supports.
    /// </summary>
    public int MaxChunkChars { get; set; } = 2_000;

    /// <summary>
    /// How many chunk hits to ask the vector store for per conversation wanted. Chunks of the same
    /// conversation compete for the same top-k, so a multiplier above 1 is what stops one conversation's
    /// chunks crowding every other conversation out of the candidate set. Default is 4.
    /// </summary>
    public int ChunkCandidateMultiplier { get; set; } = 4;

    /// <summary>
    /// How many conversations are retained for generation after ranking — the explicit breadth of the
    /// context, rather than a side effect of top-k (ADR-016). Cutting to a single conversation is a
    /// regression for any question whose answer spans conversations, so the default retains several (4).
    /// A caller asking for fewer still gets up to this many when that many clear
    /// <see cref="RetainedRelativeScore"/>; a caller asking for more gets what it asked for.
    /// </summary>
    public int RetainedConversations { get; set; } = 4;

    /// <summary>
    /// The fraction of the best score a conversation must reach to be retained (0–1). This is what stops
    /// a query with one clearly dominant conversation from padding the context with weak matches, which
    /// is the cost of retaining breadth. Set to 0 to retain purely by count. Default is 0.6.
    /// </summary>
    /// <remarks>
    /// Relative, not absolute, because scores are not comparable across stores or between the cosine and
    /// reranker scales. Ignored when the best score is not positive, where a ratio is meaningless.
    /// </remarks>
    public float RetainedRelativeScore { get; set; } = 0.6f;

    /// <summary>
    /// How many neighbouring chunks either side of a matching chunk are included in the generation
    /// context (read-time neighbour expansion). 0 disables expansion; the default is 1.
    /// </summary>
    /// <remarks>
    /// Chunks are stored without overlap, which is only defensible because this exists: nothing else
    /// covers a thought that spans several turns (ADR-016). Overlapping windows are merged, so a
    /// conversation with two nearby matches contributes each message once.
    /// </remarks>
    public int NeighbourWindow { get; set; } = 1;
}
