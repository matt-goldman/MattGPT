namespace MattGPT.Contracts.Models;

/// <summary>
/// The result of parsing and linearising a single ChatGPT conversation.
/// </summary>
public class ParsedConversation
{
    /// <summary>Unique conversation identifier.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Human-readable conversation title.</summary>
    public string? Title { get; set; }

    /// <summary>Unix timestamp when the conversation was created.</summary>
    public double? CreateTime { get; set; }

    /// <summary>Unix timestamp when the conversation was last updated.</summary>
    public double? UpdateTime { get; set; }

    /// <summary>The default model slug used in this conversation.</summary>
    public string? DefaultModelSlug { get; set; }

    /// <summary>Identifies which custom GPT was used (null for standard conversations).</summary>
    public string? GizmoId { get; set; }

    /// <summary>Type of GPT: "gpt" (custom GPT), "snorlax" (project), or null (standard ChatGPT).</summary>
    public string? GizmoType { get; set; }

    /// <summary>Project/template association for the conversation.</summary>
    public string? ConversationTemplateId { get; set; }

    /// <summary>Whether the user opted out of memory for this conversation.</summary>
    public bool? IsDoNotRemember { get; set; }

    /// <summary>Memory scope for this conversation (e.g. "global_enabled", "project_enabled").</summary>
    public string? MemoryScope { get; set; }

    /// <summary>Whether this conversation has been archived.</summary>
    public bool? IsArchived { get; set; }

    /// <summary>
    /// The active thread of messages in chronological order,
    /// linearised from the conversation message tree by following parent pointers
    /// from <c>current_node</c> back to the root and then reversing.
    /// </summary>
    public List<Message> Messages { get; set; } = new();
}
