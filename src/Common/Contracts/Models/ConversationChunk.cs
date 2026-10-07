using System.Text.Json.Serialization;

namespace MattGPT.Contracts.Models;

/// <summary>
/// How a conversation is divided into the chunks that are embedded and matched against.
/// </summary>
/// <remarks>
/// <para>
/// Chunks are evidence of conversation relevance, not retrieval results (ADR-016): a chunk is small
/// and discriminative, its score pools to its conversation by maximum, and the conversation is what
/// is ranked, returned and cited. The strategy therefore affects which conversations are found, not
/// what is returned.
/// </para>
/// <para>
/// A conversation's vectors are only comparable with vectors built under the same strategy, so the
/// strategy a conversation was embedded under is recorded on it
/// (<see cref="StoredConversation.EmbeddedChunkingStrategy"/>) and changing the configured strategy
/// requires a full re-embed. Serialised as a string, for the same reason as
/// <see cref="ConversationProcessingStatus"/>.
/// </para>
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ChunkingStrategy
{
    /// <summary>
    /// One chunk per conversation: the chunk and the conversation are the same object. Pooling and
    /// read-time expansion are no-ops under this strategy, and the reranker has only the
    /// conversation-level signal to work with.
    /// </summary>
    WholeConversation,

    /// <summary>One chunk per visible message.</summary>
    Message,

    /// <summary>
    /// One chunk per exchange: a user turn plus the assistant (and tool) turns that follow it, up to
    /// the next user turn. Justified by interpretability — a user turn alone is frequently
    /// uninterpretable, and pairing it with the response makes the chunk self-describing. No claim is
    /// made that an exchange contains a complete decision.
    /// </summary>
    Exchange,
}

/// <summary>
/// One embeddable piece of a conversation, with the address needed to find it again.
/// </summary>
/// <remarks>
/// The address model is shared with the claim pipeline so the two do not invent separate schemes:
/// source (<paramref name="ConversationId"/>), ordinal, parent ordinal, actor and timestamp. The
/// message-index range is what makes a chunk re-renderable from the stored conversation and what
/// read-time neighbour expansion widens.
/// </remarks>
/// <param name="ConversationId">The conversation this chunk came from.</param>
/// <param name="Ordinal">
/// Zero-based position of this chunk in its conversation's chunk list, contiguous and in document
/// order. This is what a vector-store hit carries back, and what neighbour expansion steps over.
/// </param>
/// <param name="StartMessageIndex">
/// Index into <see cref="StoredConversation.LinearisedMessages"/> of the first message in this chunk.
/// </param>
/// <param name="EndMessageIndex">Index of the last message in this chunk (inclusive).</param>
/// <param name="EmbeddingText">
/// The text that is embedded for this chunk, including any augmentation prefix. Not the text shown
/// to a model at generation time — that is rendered from the message range, so the augmentation is
/// not repeated for every chunk of a conversation.
/// </param>
/// <param name="Strategy">The strategy that produced this chunk.</param>
/// <param name="ParentOrdinal">
/// When a unit was too long for one chunk and had to be split, the ordinal of the first chunk of that
/// unit; <c>null</c> for a chunk that is a whole unit (including the first part of a split one).
/// </param>
/// <param name="Actor">The role that owns the chunk ("user", "assistant", …), or null when mixed.</param>
/// <param name="Timestamp">Creation time of the chunk's first message, as a Unix timestamp in seconds.</param>
public record ConversationChunk(
    string ConversationId,
    int Ordinal,
    int StartMessageIndex,
    int EndMessageIndex,
    string EmbeddingText,
    ChunkingStrategy Strategy,
    int? ParentOrdinal = null,
    string? Actor = null,
    double? Timestamp = null);

/// <summary>One chunk's embedding vector, ready to be stored.</summary>
public record ChunkVector(ConversationChunk Chunk, float[] Vector);
