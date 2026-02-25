namespace MattGPT.ApiService.Models;

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

    /// <summary>
    /// The active thread of messages in chronological order,
    /// linearised from the conversation message tree by following parent pointers
    /// from <c>current_node</c> back to the root and then reversing.
    /// </summary>
    public List<Message> Messages { get; set; } = new();
}
