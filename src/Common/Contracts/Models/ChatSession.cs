using System.Text.Json.Serialization;

namespace MattGPT.Contracts.Models;

/// <summary>Lifecycle status of a chat session.</summary>
/// <remarks>
/// Serialised as a string in JSON. Load-bearing for Postgres: inserts serialise the whole document,
/// while status updates write <c>data.status</c> with <c>jsonb_set</c> as a string, so without a
/// string contract on the enum a session becomes unreadable after its first status change.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ChatSessionStatus { Active, Completed }

/// <summary>Whether a session's current content is in the memory store (ADR-013).</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ChatSessionEmbeddingStatus
{
    /// <summary>The current content has not been queued for embedding (the session is active, or predates embedding).</summary>
    None,

    /// <summary>Completed and waiting to be summarised, projected and embedded.</summary>
    Pending,

    /// <summary>The completed session's projection is embedded.</summary>
    Embedded,

    /// <summary>Projection or embedding failed. Retried by the backfill in <c>POST /conversations/embed</c>.</summary>
    Error,
}

/// <summary>
/// A past conversation an assistant message drew on, stored with the message so it can be shown
/// again when the session is reopened.
/// </summary>
public class ChatSessionMessageSource
{
    public string ConversationId { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string? Summary { get; set; }

    /// <summary>Relevance score at the time of the response (cosine similarity, or reranker relevance when reranking).</summary>
    public float Score { get; set; }

    /// <summary>Whether the source is an imported conversation or an earlier chat session.</summary>
    public ConversationSource Source { get; set; } = ConversationSource.Import;
}

/// <summary>
/// A single message within a chat session, stored in MongoDB.
/// </summary>
public class ChatSessionMessage
{
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Past conversations an assistant message drew on. <c>null</c> for user messages, assistant
    /// messages with no sources, and messages stored before sources were persisted.
    /// </summary>
    public List<ChatSessionMessageSource>? Sources { get; set; }
}

/// <summary>
/// A persisted chat session stored as a MongoDB document.
/// Tracks all messages, the rolling summary, and session lifecycle status. Completed sessions are
/// projected into a <see cref="StoredConversation"/> and embedded (ADR-013).
/// </summary>
public class ChatSession
{
    /// <summary>Unique session identifier, used as the MongoDB document _id.</summary>
    public Guid SessionId { get; set; } = Guid.NewGuid();

    /// <summary>Auto-generated title derived from the first user message.</summary>
    public string? Title { get; set; }

    /// <summary>All messages in chronological order.</summary>
    public List<ChatSessionMessage> Messages { get; set; } = [];

    /// <summary>
    /// LLM-compressed summary of older messages in this session.
    /// Used as medium-term memory in the three-tier prompt model.
    /// </summary>
    public string? RollingSummary { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public ChatSessionStatus Status { get; set; } = ChatSessionStatus.Active;

    /// <summary>When the session was last completed; null while it has never been completed.</summary>
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>
    /// LLM summary of the whole session, generated when it completes. Unlike
    /// <see cref="RollingSummary"/> (older messages only, for the prompt) this covers every message.
    /// Cleared when the session is reactivated, because it no longer covers the whole session.
    /// </summary>
    public string? Summary { get; set; }

    /// <summary>Whether the session's current content is in the memory store.</summary>
    public ChatSessionEmbeddingStatus EmbeddingStatus { get; set; } = ChatSessionEmbeddingStatus.None;

    /// <summary>
    /// The Identity user ID of the owner, or <c>null</c> for sessions created without authentication.
    /// Used to scope chat sessions to individual users when auth is enabled.
    /// </summary>
    public string? UserId { get; set; }
}
