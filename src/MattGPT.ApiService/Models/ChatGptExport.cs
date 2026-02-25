using System.Text.Json.Serialization;

namespace MattGPT.ApiService.Models;

/// <summary>
/// A single conversation from the ChatGPT export.
/// </summary>
public class Conversation
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("create_time")]
    public double? CreateTime { get; set; }

    [JsonPropertyName("update_time")]
    public double? UpdateTime { get; set; }

    [JsonPropertyName("mapping")]
    public Dictionary<string, MappingNode> Mapping { get; set; } = new();

    [JsonPropertyName("current_node")]
    public string? CurrentNode { get; set; }

    [JsonPropertyName("default_model_slug")]
    public string? DefaultModelSlug { get; set; }
}

/// <summary>
/// A node in the conversation message tree.
/// </summary>
public class MappingNode
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("message")]
    public Message? Message { get; set; }

    [JsonPropertyName("parent")]
    public string? Parent { get; set; }

    [JsonPropertyName("children")]
    public List<string> Children { get; set; } = new();
}

/// <summary>
/// A single message in the conversation.
/// </summary>
public class Message
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("author")]
    public Author Author { get; set; } = new();

    [JsonPropertyName("content")]
    public Content Content { get; set; } = new();

    [JsonPropertyName("create_time")]
    public double? CreateTime { get; set; }
}

/// <summary>
/// The author of a message.
/// </summary>
public class Author
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("metadata")]
    public Dictionary<string, object?>? Metadata { get; set; }
}

/// <summary>
/// The content of a message. Structure varies by content_type.
/// </summary>
public class Content
{
    [JsonPropertyName("content_type")]
    public string ContentType { get; set; } = string.Empty;

    /// <summary>
    /// Used by 'text' and 'multimodal_text' content types.
    /// Each element is either a plain string or a JSON element (e.g. image_asset_pointer).
    /// </summary>
    [JsonPropertyName("parts")]
    public List<System.Text.Json.JsonElement>? Parts { get; set; }

    /// <summary>
    /// Text content used by 'code', 'execution_output', 'system_error', and 'tether_quote' content types.
    /// </summary>
    [JsonPropertyName("text")]
    public string? Text { get; set; }
}
