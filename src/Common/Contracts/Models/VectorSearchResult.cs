namespace MattGPT.Contracts.Models;

/// <summary>
/// Which chunk of a conversation a vector hit was for, as stored alongside the vector.
/// </summary>
/// <remarks>
/// Carried back from the store so chunk scores can be pooled to the conversation and so the matching
/// chunks can be re-rendered (and expanded to their neighbours) from the stored conversation. A hit
/// from a vector written before chunking existed has no chunk payload, so this is nullable.
/// </remarks>
/// <param name="Ordinal">The chunk's position in its conversation (see <see cref="ConversationChunk.Ordinal"/>).</param>
/// <param name="ChunkCount">How many chunks the conversation was split into when it was embedded.</param>
/// <param name="StartMessageIndex">Index of the chunk's first message in the conversation.</param>
/// <param name="EndMessageIndex">Index of the chunk's last message (inclusive).</param>
/// <param name="Strategy">The strategy the chunk was produced under.</param>
public record ChunkHit(
    int Ordinal,
    int ChunkCount,
    int StartMessageIndex,
    int EndMessageIndex,
    ChunkingStrategy Strategy);

/// <summary>
/// A search result from a vector similarity search, containing the matched conversation's ID, similarity score, and optionally its title and summary for display purposes.
/// </summary>
/// <param name="ConversationId">The ID of the matched conversation.</param>
/// <param name="Score">The similarity score of the match.</param>
/// <param name="Title">The title of the matched conversation, if available.</param>
/// <param name="Summary">The summary of the matched conversation, if available.</param>
/// <param name="Chunk">
/// Which chunk matched, when the store holds chunk-level vectors. Null for a vector with no chunk
/// payload, which is treated as the whole conversation.
/// </param>
public record VectorSearchResult(
    string ConversationId,
    float Score,
    string? Title,
    string? Summary,
    ChunkHit? Chunk = null);
