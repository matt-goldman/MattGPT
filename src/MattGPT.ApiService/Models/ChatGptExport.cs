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

    /// <summary>Identifies which custom GPT was used (null for standard conversations).</summary>
    [JsonPropertyName("gizmo_id")]
    public string? GizmoId { get; set; }

    /// <summary>Type of GPT: "gpt" (custom GPT), "snorlax" (project), or null (standard ChatGPT).</summary>
    [JsonPropertyName("gizmo_type")]
    public string? GizmoType { get; set; }

    /// <summary>Project/template association for the conversation.</summary>
    [JsonPropertyName("conversation_template_id")]
    public string? ConversationTemplateId { get; set; }

    /// <summary>Whether the user opted out of memory for this conversation.</summary>
    [JsonPropertyName("is_do_not_remember")]
    public bool? IsDoNotRemember { get; set; }

    /// <summary>Memory scope for this conversation (e.g. "global_enabled", "project_enabled").</summary>
    [JsonPropertyName("memory_scope")]
    public string? MemoryScope { get; set; }

    /// <summary>Whether this conversation has been archived.</summary>
    [JsonPropertyName("is_archived")]
    public bool? IsArchived { get; set; }
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

    /// <summary>
    /// Weight/priority of this message. Typically 0.0 for system messages, 1.0 for visible messages.
    /// </summary>
    [JsonPropertyName("weight")]
    public double? Weight { get; set; }

    /// <summary>
    /// Message-level metadata containing model info, visibility flags, citations, etc.
    /// </summary>
    [JsonPropertyName("metadata")]
    public MessageMetadata? Metadata { get; set; }
}

/// <summary>
/// Metadata associated with a message. Contains visibility flags and other contextual information.
/// </summary>
public class MessageMetadata
{
    /// <summary>Whether this message is hidden in the UI (common for system messages).</summary>
    [JsonPropertyName("is_visually_hidden_from_conversation")]
    public bool? IsVisuallyHiddenFromConversation { get; set; }

    [JsonPropertyName("citations")]
    public List<MessageCitation>? Citations { get; set; }

    [JsonPropertyName("content_references")]
    public List<MessageContentReference>? ContentReferences { get; set; }
}

/// <summary>A citation from message metadata.</summary>
public class MessageCitation
{
    [JsonPropertyName("start_ix")]
    public int? StartIndex { get; set; }

    [JsonPropertyName("end_ix")]
    public int? EndIndex { get; set; }

    [JsonPropertyName("citation_format_type")]
    public string? FormatType { get; set; }

    [JsonPropertyName("metadata")]
    public CitationMetadata? Metadata { get; set; }
}

/// <summary>Metadata within a citation.</summary>
public class CitationMetadata
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }
}

/// <summary>A content reference from message metadata.</summary>
public class MessageContentReference
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("matched_text")]
    public string? MatchedText { get; set; }

    [JsonPropertyName("snippet")]
    public string? Snippet { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("source")]
    public string? Source { get; set; }
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

    /// <summary>Programming language for 'code' content type.</summary>
    [JsonPropertyName("language")]
    public string? Language { get; set; }

    /// <summary>Browse result text for 'tether_browsing_display' content type.</summary>
    [JsonPropertyName("result")]
    public string? Result { get; set; }

    /// <summary>Summary text for 'tether_browsing_display' content type.</summary>
    [JsonPropertyName("summary")]
    public string? Summary { get; set; }

    /// <summary>Source URL for 'tether_quote' content type.</summary>
    [JsonPropertyName("url")]
    public string? Url { get; set; }

    /// <summary>Source domain for 'tether_quote' content type.</summary>
    [JsonPropertyName("domain")]
    public string? Domain { get; set; }

    /// <summary>Source title for 'tether_quote' content type.</summary>
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    /// <summary>Thought items for 'thoughts' content type.</summary>
    [JsonPropertyName("thoughts")]
    public List<ThoughtItem>? Thoughts { get; set; }

    /// <summary>Text content for 'reasoning_recap' content type (JSON field: "content").</summary>
    [JsonPropertyName("content")]
    public string? ReasoningContent { get; set; }

    /// <summary>User profile text for 'user_editable_context' content type.</summary>
    [JsonPropertyName("user_profile")]
    public string? UserProfile { get; set; }

    /// <summary>User instructions text for 'user_editable_context' content type.</summary>
    [JsonPropertyName("user_instructions")]
    public string? UserInstructions { get; set; }

    /// <summary>Output string for 'citable_code_output' content type.</summary>
    [JsonPropertyName("output_str")]
    public string? OutputStr { get; set; }

    /// <summary>Error name for 'system_error' content type.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>Browser/computer state for 'computer_output' content type.</summary>
    [JsonPropertyName("state")]
    public ComputerState? State { get; set; }
}

/// <summary>
/// A single thought item within a 'thoughts' content type message.
/// </summary>
public class ThoughtItem
{
    [JsonPropertyName("content")]
    public string? Content { get; set; }

    [JsonPropertyName("summary")]
    public string? Summary { get; set; }
}

/// <summary>
/// Browser/computer state from a 'computer_output' content type message.
/// </summary>
public class ComputerState
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }
}
