using MongoDB.Bson.Serialization.Attributes;

namespace MattGPT.ApiService.Models;

/// <summary>Processing status of a stored conversation.</summary>
public enum ConversationProcessingStatus { Imported, Summarised, Embedded }

/// <summary>
/// A single message as stored in MongoDB, with parts normalised to strings.
/// </summary>
public class StoredMessage
{
    public string Id { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public List<string> Parts { get; set; } = new();
    public double? CreateTime { get; set; }

    internal static StoredMessage From(Message message) => new()
    {
        Id = message.Id,
        Role = message.Author.Role,
        ContentType = message.Content.ContentType,
        Parts = message.Content.Parts?
            .Select(p => p.ValueKind == System.Text.Json.JsonValueKind.String
                ? p.GetString() ?? string.Empty
                : p.GetRawText())
            .ToList() ?? new List<string>(),
        CreateTime = message.CreateTime,
    };
}

/// <summary>
/// A parsed ChatGPT conversation stored as a MongoDB document.
/// <c>ConversationId</c> is used as the document <c>_id</c> to guarantee uniqueness.
/// </summary>
public class StoredConversation
{
    /// <summary>The original conversation ID from the ChatGPT export, used as the MongoDB document _id.</summary>
    [BsonId]
    public string ConversationId { get; set; } = string.Empty;

    public string? Title { get; set; }
    public double? CreateTime { get; set; }
    public double? UpdateTime { get; set; }
    public string? DefaultModelSlug { get; set; }

    /// <summary>Active-thread messages in chronological order, linearised from the message tree.</summary>
    public List<StoredMessage> LinearisedMessages { get; set; } = new();

    /// <summary>UTC timestamp when this document was last imported.</summary>
    public DateTimeOffset ImportTimestamp { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Tracks how far through the RAG pipeline this conversation has been processed.</summary>
    public ConversationProcessingStatus ProcessingStatus { get; set; } = ConversationProcessingStatus.Imported;

    internal static StoredConversation From(ParsedConversation conversation) => new()
    {
        ConversationId = conversation.Id,
        Title = conversation.Title,
        CreateTime = conversation.CreateTime,
        UpdateTime = conversation.UpdateTime,
        DefaultModelSlug = conversation.DefaultModelSlug,
        LinearisedMessages = conversation.Messages.Select(StoredMessage.From).ToList(),
        ImportTimestamp = DateTimeOffset.UtcNow,
        ProcessingStatus = ConversationProcessingStatus.Imported,
    };
}
