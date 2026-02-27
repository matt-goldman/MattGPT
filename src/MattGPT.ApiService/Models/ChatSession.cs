using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace MattGPT.ApiService.Models;

/// <summary>Lifecycle status of a chat session.</summary>
public enum ChatSessionStatus { Active, Completed }

/// <summary>
/// A single message within a chat session, stored in MongoDB.
/// </summary>
public class ChatSessionMessage
{
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// A persisted chat session stored as a MongoDB document.
/// Tracks all messages, the rolling summary, and session lifecycle status.
/// Designed for future extension with embedding fields (issue 019).
/// </summary>
public class ChatSession
{
    /// <summary>Unique session identifier, used as the MongoDB document _id.</summary>
    [BsonId]
    [BsonRepresentation(BsonType.String)]
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
}
